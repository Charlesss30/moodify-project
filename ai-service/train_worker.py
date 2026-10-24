"""Local fine-tuning worker. Fixed CLI, no network downloads, no serving mutation."""
import argparse
import hashlib
import json
import math
import os
import random
from pathlib import Path


def digest(path):
    h = hashlib.sha256()
    with open(path, 'rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()


def metrics(probabilities, targets, thresholds):
    import torch
    predicted = probabilities >= torch.tensor(thresholds)
    actual = targets.bool()
    tp = (predicted & actual).sum(0).float()
    fp = (predicted & ~actual).sum(0).float()
    fn = (~predicted & actual).sum(0).float()
    f1 = 2 * tp / (2 * tp + fp + fn).clamp(min=1)
    micro = (2 * tp.sum() / (2 * tp.sum() + fp.sum() + fn.sum()).clamp(min=1)).item()
    return micro, f1.mean().item()


def atomic_json(path, value):
    temp = path.with_suffix('.tmp')
    temp.write_text(json.dumps(value, allow_nan=False), encoding='utf-8')
    os.replace(temp, path)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--parent-pid', type=int)
    parser.add_argument('--job-dir', required=True)
    parser.add_argument('--model-dir', required=True)
    parser.add_argument('--device', choices=['cpu', 'cuda'], default='cpu')
    args = parser.parse_args()
    # Die with the backend even after an abrupt host termination (no orphan training).
    if args.parent_pid:
        import threading
        import time
        def watch_parent():
            if os.name == 'nt':
                import ctypes
                kernel = ctypes.WinDLL('kernel32', use_last_error=True)
                kernel.OpenProcess.restype = ctypes.c_void_p
                kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
                handle = kernel.OpenProcess(0x00100000, False, args.parent_pid)
                if not handle: os._exit(70)
                if kernel.WaitForSingleObject(handle, 0xFFFFFFFF) == 0: os._exit(70)
            else:
                while os.getppid() == args.parent_pid: time.sleep(1)
                os._exit(70)
        threading.Thread(target=watch_parent,daemon=True).start()
    work = Path(args.job_dir).resolve()
    model_dir = Path(args.model_dir).resolve()
    if work == model_dir or model_dir in work.parents or work in model_dir.parents:
        raise ValueError('Training output must be separate from serving model.')
    bundle = json.loads((work / 'bundle.json').read_text(encoding='utf-8-sig'))
    config_text, data_text = bundle['configJson'], bundle['datasetJson']
    for text, key in [(config_text, 'configHash'), (data_text, 'datasetHash')]:
        if hashlib.sha256(text.encode()).hexdigest() != bundle[key]:
            raise ValueError('Bundle provenance mismatch.')
    config = json.loads(config_text)
    rows = json.loads(data_text)
    labels = bundle['labels']
    if len(labels) != 28 or len(set(labels)) != 28:
        raise ValueError('Expected canonical 28-label classifier.')
    seen = set()
    for row in rows:
        normalized = ' '.join(row['text'].split()).lower()
        if not normalized or normalized in seen or row['split'] not in ('train', 'validation', 'test') or not row['labels'] or any(x not in labels for x in row['labels']):
            raise ValueError('Invalid sample or cross-split leakage.')
        seen.add(normalized)
    def progress(stage, **values):
        payload = {'stage': stage, **values}
        atomic_json(work / 'progress.json', payload)
        print(json.dumps(payload), flush=True)
    progress('loading', device=args.device)
    import torch
    from torch.utils.data import DataLoader, Dataset
    from transformers import AutoTokenizer, AutoModelForSequenceClassification
    torch.set_num_threads(max(1, min(4, (os.cpu_count() or 2) // 2)))
    random.seed(config['seed'])
    torch.manual_seed(config['seed'])
    if args.device == 'cuda' and not torch.cuda.is_available():
        raise ValueError('CUDA requested but unavailable.')
    device = torch.device(args.device)
    # Full fine-tuning needs optimizer/gradient/activation memory in addition to weights.
    weight_bytes = (model_dir / 'model.safetensors').stat().st_size
    required = weight_bytes * 10 + 512 * 1024**2
    if device.type == 'cuda':
        available, _ = torch.cuda.mem_get_info(device)
    elif os.name == 'nt':
        import ctypes
        class MemoryStatus(ctypes.Structure):
            _fields_ = [('length',ctypes.c_ulong),('load',ctypes.c_ulong),
                        ('total',ctypes.c_ulonglong),('available',ctypes.c_ulonglong),
                        ('pageTotal',ctypes.c_ulonglong),('pageAvailable',ctypes.c_ulonglong),
                        ('virtualTotal',ctypes.c_ulonglong),('virtualAvailable',ctypes.c_ulonglong),('extended',ctypes.c_ulonglong)]
        status=MemoryStatus();status.length=ctypes.sizeof(status)
        if not ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)):
            raise RuntimeError('Cannot check memory before training.')
        available=status.available
    else:
        available=os.sysconf('SC_AVPHYS_PAGES')*os.sysconf('SC_PAGE_SIZE')
    if available < required:
        raise RuntimeError(f'Insufficient free memory: need about {required/1024**3:.1f} GiB, available {available/1024**3:.1f} GiB. Use Colab or a dedicated training machine.')
    tokenizer = AutoTokenizer.from_pretrained(model_dir, local_files_only=True)
    model = AutoModelForSequenceClassification.from_pretrained(model_dir, local_files_only=True)
    if [model.config.id2label[i] for i in range(28)] != labels:
        raise ValueError('Base model label order mismatch.')
    model.to(device)
    model.gradient_checkpointing_enable()
    # Continued fine-tuning, not a fresh pretrained-baseline experiment.
    splits = {name: [r for r in rows if r['split'] == name] for name in ('train','validation','test')}
    if not splits['train'] or not splits['test'] or len(splits['validation']) < 400:
        raise ValueError('Need train/test and >=400 validation samples.')
    random.Random(config['seed']).shuffle(splits['validation'])
    cut = len(splits['validation']) // 2
    selection, calibration = splits['validation'][:cut], splits['validation'][cut:]
    class Samples(Dataset):
        def __init__(self, data): self.data = data
        def __len__(self): return len(self.data)
        def __getitem__(self, i):
            row = self.data[i]
            tokens = tokenizer(row['text'], max_length=128, truncation=True, padding='max_length', return_tensors='pt')
            return {k: v[0] for k, v in tokens.items()}, torch.tensor([float(label in row['labels']) for label in labels])
    def loader(data, shuffle=False): return DataLoader(Samples(data), batch_size=config['batchSize'], shuffle=shuffle, num_workers=0)
    @torch.no_grad()
    def evaluate(data):
        model.eval()
        ps, ys = [], []
        for tokens, target in loader(data):
            ps.append(model(**{k:v.to(device) for k,v in tokens.items()}).logits.sigmoid().cpu())
            ys.append(target)
        return torch.cat(ps), torch.cat(ys)
    frequencies = torch.tensor([sum(label in r['labels'] for r in splits['train']) for label in labels], device=device)
    weights = ((len(splits['train']) - frequencies) / frequencies.clamp(min=1)).clamp(1,4)
    optimizer = torch.optim.AdamW(model.parameters(), lr=config['learningRate'])
    best = -1.0
    output = work / 'artifact'
    epochs = config['epochs']
    train_loader = loader(splits['train'], True)
    accumulation = config['gradientAccumulation']
    for epoch in range(epochs):
        model.train()
        optimizer.zero_grad(set_to_none=True)
        losses = []
        for step, (tokens, target) in enumerate(train_loader):
            target = target.to(device)
            logits = model(**{k:v.to(device) for k,v in tokens.items()}).logits
            positive = logits.sigmoid()
            negative = (1-positive+0.05).clamp(max=1)
            loss = -(weights*target*positive.clamp(min=1e-8).log() + (1-target)*negative.clamp(min=1e-8).log()*(1-negative).pow(2)).mean()
            if not torch.isfinite(loss): raise ValueError('Non-finite training loss.')
            # Last partial accumulation window uses its actual size.
            window = min(accumulation, len(train_loader) - (step // accumulation)*accumulation)
            (loss/window).backward()
            if (step+1) % accumulation == 0 or step+1 == len(train_loader):
                torch.nn.utils.clip_grad_norm_(model.parameters(),1.0)
                optimizer.step(); optimizer.zero_grad(set_to_none=True)
            losses.append(loss.item())
            if step % 10 == 0 or step+1 == len(train_loader):
                progress('training', epoch=epoch+1, epochs=epochs, step=step+1, steps=len(train_loader), loss=sum(losses)/len(losses))
        p,y = evaluate(selection)
        micro,macro = metrics(p,y,[0.5]*28)
        progress('selection',epoch=epoch+1,epochs=epochs,loss=sum(losses)/len(losses),microF1=micro,macroF1=macro)
        if macro > best:
            best=macro
            model.save_pretrained(output, safe_serialization=True)
            tokenizer.save_pretrained(output)
    del model,optimizer
    if device.type=='cuda': torch.cuda.empty_cache()
    model=AutoModelForSequenceClassification.from_pretrained(output,local_files_only=True).to(device)
    progress('calibration')
    p,y=evaluate(calibration)
    thresholds=[]
    for index in range(28):
        grid=[i/20 for i in range(1,20)]
        thresholds.append(max(grid,key=lambda t:metrics(p[:,index:index+1],y[:,index:index+1],[t])[1]) if y[:,index].sum()>0 else 1.0)
    progress('test')
    p,y=evaluate(splits['test'])
    micro,macro=metrics(p,y,thresholds)
    # Do not inherit the serving model's selective policy as if validated for this run.
    inference={'labels':labels,'thresholds':thresholds,'max_length':128,'language':'en','selective_policy':{'enabled':False,'threshold':1.0}}
    atomic_json(output/'inference_config.json',inference)
    report={'jobId':bundle['jobId'],'modelVersion':bundle['modelVersion'],'datasetHash':bundle['datasetHash'],'configHash':bundle['configHash'],
            'artifactHash':digest(output/'model.safetensors'),'microF1':micro,'macroF1':macro,'testSamples':len(splits['test'])}
    atomic_json(output/'training_provenance.json',{'baseArtifactHash':digest(model_dir/'model.safetensors'),'strategy':'continued_finetuning_asl','datasetHash':bundle['datasetHash'],
        'configHash':bundle['configHash'],'independentQualityVerified':False,'selectivePolicyValidated':False})
    atomic_json(output/'evaluation_summary.json',{'run_id':bundle['modelVersion'],'micro_f1':micro,'macro_f1':macro,'benchmark_status':'independence_not_verified'})
    atomic_json(work/'report.json',report)
    progress('completed',microF1=micro,macroF1=macro,artifactHash=report['artifactHash'])

if __name__=='__main__':
    main()

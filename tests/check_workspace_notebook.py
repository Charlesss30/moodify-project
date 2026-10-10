import ast,json,tempfile,types,sys
from pathlib import Path
n=json.loads(Path('.local-build/ai-workspace/generated-notebook.test.ipynb').read_text(encoding='utf-8'))
for i,c in enumerate(n['cells']):
    source=''.join(c['source'])
    source='\n'.join(line for line in source.splitlines() if not line.startswith(('%','!')))
    ast.parse(source,filename=f'cell-{i}')
# Execute only generated data/config bridge in an isolated directory; no training/import of torch.
colab=types.ModuleType('google.colab'); colab.drive=types.SimpleNamespace(mount=lambda _:None)
sys.modules['google.colab']=colab
with tempfile.TemporaryDirectory() as directory:
    source=''.join(n['cells'][1]['source']).replace("Path('/content/drive/MyDrive/train-KLCN')",f'Path({directory!r})')
    scope={};exec(compile(source,'bridge','exec'),scope)
    assert scope['EPOCHS']==6 and scope['GRAD_ACCUM']==16 and scope['LOSS_KIND']=='asl'
    assert scope['DATA_DIR'].is_relative_to(Path(directory))
    assert len((scope['DATA_DIR']/'validation.csv').read_text().splitlines())==401
    assert (scope['DATA_DIR']/'train.csv').exists() and (scope['DATA_DIR']/'test.csv').exists()
print('PASS: all notebook cells parse; bridge verifies checksums, isolated paths, ASL config and CSV splits. No training executed.')

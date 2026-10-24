"""Create tiny random RoBERTa fixture; never an application model."""
from pathlib import Path
import json
from transformers import RobertaConfig, RobertaForSequenceClassification, AutoTokenizer
root=Path(__file__).resolve().parents[1]
output=root/'.local-build/training-tiny-fixture'
output.mkdir(parents=True,exist_ok=True)
labels=[x['label'] for x in json.loads((root/'backend/Data/emotion-mood-mapping.json').read_text(encoding='utf-8-sig'))['labels']]
model=RobertaForSequenceClassification(RobertaConfig(vocab_size=50265,hidden_size=8,num_hidden_layers=1,num_attention_heads=2,intermediate_size=16,num_labels=28,max_position_embeddings=514,id2label=dict(enumerate(labels)),label2id={x:i for i,x in enumerate(labels)}))
model.save_pretrained(output)
AutoTokenizer.from_pretrained(root/'code-train/train-KLCN/output/roberta_large_asl_en_01/best_model',local_files_only=True).save_pretrained(output)
print('Tiny random training fixture ready:',output)

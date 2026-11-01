"""Shared training/serving normalization. Keep this version with every new candidate."""
import re

VERSION = "vsmec-normalize-v1"
DEFAULT_EMOJI = {"😂": "cười", "😍": "yêu thích", "😭": "khóc buồn", "😱": "sợ hãi"}


def normalize(text, emoji_dict, teencode_dict, word_tokenizer):
    text = text.strip()
    for emoji, description in sorted(emoji_dict.items(), key=lambda item: len(item[0]), reverse=True):
        text = text.replace(emoji, f" {description} ")
    text = re.sub(r"[^\w\s]", " ", text, flags=re.UNICODE).lower()
    text = " ".join(teencode_dict.get(word, word) for word in text.split())
    text = re.sub(r"\s+", " ", text).strip()
    if not text:
        raise ValueError("Text is empty after Vietnamese text normalization.")
    return word_tokenizer.tokenize(text).lower()


def validate(payload):
    if not isinstance(payload, dict) or payload.get("normalization_version", VERSION) != VERSION:
        raise ValueError("Unsupported Vietnamese preprocessing version.")
    for key in ("emoji_dict", "teencode_dict"):
        mapping = payload.get(key)
        if not isinstance(mapping, dict) or len(mapping) > 200_000:
            raise ValueError("Preprocessing requires bounded emoji_dict and teencode_dict objects.")
        if any(not isinstance(k, str) or not k.strip() or len(k) > 200
               or not isinstance(v, str) or not v.strip() or len(v) > 1000 for k, v in mapping.items()):
            raise ValueError("Preprocessing dictionaries require nonempty string keys and values.")
    return payload
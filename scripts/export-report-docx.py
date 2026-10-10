"""Export the week 7/8 Markdown report as editable Word, using stdlib only."""
from pathlib import Path
from xml.sax.saxutils import escape
from zipfile import ZipFile, ZIP_DEFLATED
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
source = root / 'docs/BAO_CAO_TUAN_7_8.md'
target = source.with_suffix('.docx')
ns = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'

def paragraph(text, style='Normal'):
    text = text.replace('**', '').replace('`', '')
    return (f'<w:p><w:pPr><w:pStyle w:val="{style}"/></w:pPr>'
            f'<w:r><w:t xml:space="preserve">{escape(text)}</w:t></w:r></w:p>')

body = []
for line in source.read_text(encoding='utf-8-sig').splitlines():
    line = line.strip()
    if not line:
        continue
    if line.startswith('# '):
        body.append(paragraph(line[2:], 'Title'))
    elif line.startswith('## '):
        body.append(paragraph(line[3:], 'Heading1'))
    elif line.startswith('### '):
        body.append(paragraph(line[4:], 'Heading2'))
    else:
        body.append(paragraph(('\u2022 ' + line[2:]) if line.startswith('- ') else line))

styles = f'''<w:styles xmlns:w="{ns}">
<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman" w:eastAsia="Times New Roman"/><w:sz w:val="26"/><w:lang w:val="vi-VN"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="330" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults>
<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
<w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:pPr><w:jc w:val="center"/><w:keepNext/></w:pPr><w:rPr><w:b/><w:sz w:val="34"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:pPr><w:keepNext/><w:outlineLvl w:val="0"/><w:spacing w:before="240"/></w:pPr><w:rPr><w:b/><w:sz w:val="30"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:pPr><w:keepNext/><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:b/><w:sz w:val="27"/></w:rPr></w:style>
</w:styles>'''
document = (f'<w:document xmlns:w="{ns}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body>'
            + ''.join(body) + '<w:sectPr><w:footerReference w:type="default" r:id="rId2"/>'
            '<w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1134" w:right="1134" w:bottom="1134" w:left="1701" w:footer="567"/></w:sectPr></w:body></w:document>')
rels_ns = 'http://schemas.openxmlformats.org/package/2006/relationships'
rel_type = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/'
parts = {
    'word/document.xml': document,
    'word/styles.xml': styles,
    'word/footer.xml': f'<w:ftr xmlns:w="{ns}"><w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:fldSimple w:instr="PAGE"/></w:p></w:ftr>',
    '_rels/.rels': f'<Relationships xmlns="{rels_ns}"><Relationship Id="rId1" Type="{rel_type}officeDocument" Target="word/document.xml"/></Relationships>',
    'word/_rels/document.xml.rels': f'<Relationships xmlns="{rels_ns}"><Relationship Id="rId1" Type="{rel_type}styles" Target="styles.xml"/><Relationship Id="rId2" Type="{rel_type}footer" Target="footer.xml"/></Relationships>',
    '[Content_Types].xml': '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/word/footer.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/></Types>',
}
with ZipFile(target, 'w', ZIP_DEFLATED) as archive:
    for name, xml in parts.items():
        ET.fromstring(xml)
        archive.writestr(name, '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' + xml)
with ZipFile(target) as archive:
    assert archive.testzip() is None
    text = ''.join(ET.fromstring(archive.read('word/document.xml')).itertext())
    assert 'TUẦN 7' in text and 'TUẦN 8' in text and 'Reinforcement Learning' in text
print(f'Created and validated {target.relative_to(root)} ({len(body)} paragraphs).')

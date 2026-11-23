"""Mischt Alltagssprache (Chats, Nachrichten) in die eingebaute deutsche Wortliste und die Wortfolgen.

Aufruf (im Hauptordner):
    python tools/merge_alltag.py

Liest:
  tools/alltag_de_woerter.txt      Wörter, die in wordfreq/hunspell fehlen (gehts, naja, Feierabend ...)
  tools/alltag_de_wortfolgen.txt   wort<TAB>folgewort<TAB>stufe (3 = sehr oft, 2 = oft)
Schreibt:
  Resources/wortliste_de.txt und .txt.gz   fehlende Wörter mit ihrer wordfreq-Häufigkeit (Zipf x 100)
  Resources/wortfolgen_de.txt.gz           Folgewörter ergänzt, höchstens 6 pro Wort

Mehrfach ausführen schadet nicht. Nach make_wordlist.py / make_bigrams.py nochmal ausführen.
"""
import gzip
import os

from wordfreq import zipf_frequency

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = os.path.join(ROOT, "Resources")
TOOLS = os.path.join(ROOT, "tools")
PER_WORD = 6
POINTS = {3: 35, 2: 15}     # Punkte wie in make_bigrams.py (1 bis 99)


def lines(path):
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if line and not line.startswith("#"):
                yield line


# ---------- Wortliste ----------
wl_txt = os.path.join(RES, "wortliste_de.txt")
with open(wl_txt, encoding="utf-8") as f:
    wl_lines = f.read().split("\n")
while wl_lines and wl_lines[-1] == "":
    wl_lines.pop()
known = {l.split("\t")[0].lower() for l in wl_lines if "\t" in l and not l.startswith("#")}
added = []
for w in lines(os.path.join(TOOLS, "alltag_de_woerter.txt")):
    w = w.strip()
    if w.lower() in known:
        continue
    freq = max(100, int(round(zipf_frequency(w, "de") * 100)))
    wl_lines.append(w + "\t" + str(freq))
    known.add(w.lower())
    added.append(w)
# alphabetisch wie die eingebaute Liste (dann muss die Tastatur beim Start nicht sortieren)
head = [l for l in wl_lines if l.startswith("#")]
body = sorted((l for l in wl_lines if not l.startswith("#")), key=lambda l: l.split("\t")[0].lower())
text = "\n".join(head + body) + "\n"
with open(wl_txt, "w", encoding="utf-8", newline="\n") as f:
    f.write(text)
with gzip.open(wl_txt + ".gz", "wb", compresslevel=9) as f:
    f.write(text.encode("utf-8"))

# ---------- Wortfolgen ----------
wf_gz = os.path.join(RES, "wortfolgen_de.txt.gz")
order = []
nexts = {}
with gzip.open(wf_gz, "rt", encoding="utf-8") as f:
    for line in f:
        p = line.rstrip("\n").split("\t")
        if len(p) < 3:
            continue
        key = p[0]
        order.append(key)
        nexts[key] = [(p[i], int(p[i + 1])) for i in range(1, len(p) - 1, 2)]

new_pairs = 0
for line in lines(os.path.join(TOOLS, "alltag_de_wortfolgen.txt")):
    a, b, st = line.split("\t")
    pts = POINTS.get(int(st))
    if not pts or a.lower() not in known or b.lower() not in known:
        continue
    key = a.lower()
    lst = nexts.get(key)
    if lst is None:
        lst = nexts[key] = []
        order.append(key)
    for i, (form, old) in enumerate(lst):
        if form.lower() == b.lower():
            if pts > old:
                lst[i] = (form, pts)
            break
    else:
        lst.append((b, pts))
        new_pairs += 1

out = []
for key in sorted(set(order)):
    best = sorted(nexts[key], key=lambda x: -x[1])[:PER_WORD]
    out.append("\t".join([key] + [x for form, sc in best for x in (form, str(sc))]))
with gzip.open(wf_gz, "wb", compresslevel=9) as f:
    f.write(("\n".join(out) + "\n").encode("utf-8"))

print(len(added), "Wörter ergänzt:", ", ".join(added))
print(new_pairs, "Wortfolgen eingemischt (höchstens", PER_WORD, "pro Wort bleiben),", len(out), "Wörter mit Folgewörtern")

"""Groß- und Kleinschreibung in der deutschen Wortliste richtigstellen.

make_wordlist.py behält pro Wort nur eine Schreibweise, und wenn hunspell ein Wort auch klein
kennt, gewinnt die kleine. Dadurch standen viele Substantive klein in der Liste (zeit, platz,
teil, ende, vertrag), obwohl sie fast immer großgeschrieben werden. Und Wörter, die es in beiden
Schreibweisen gibt (essen/Essen, leben/Leben, morgen/Morgen), gab es nur einmal.

Dieses Skript zählt in echten Texten, wie oft ein Wort mitten im Satz groß und wie oft klein
geschrieben wird (am Satzanfang sagt die Schreibweise nichts):
- Kommt die andere Schreibweise in mindestens 90 % der Fälle vor, wird sie die Schreibweise
  in der Liste (zeit -> Zeit).
- Kommt die seltenere Schreibweise in mindestens 10 % der Fälle vor, bekommt sie eine eigene
  Zeile mit entsprechend kleinerer Häufigkeit (Essen neben essen).
Wörter mit weniger als 8 Belegen bleiben, wie sie sind.

Außerdem werden die Wortfolgen angepasst (--wortfolgen): Folgewörter bekommen die Schreibweise
aus der neuen Liste, und gibt es beide, die, die in den Texten nach dem Wort häufiger vorkommt
("beim Essen", "wir essen").

Aufruf:
    python fix_case_de.py <wortliste_de.txt> <ausgabe.txt> [--wortfolgen ein.txt.gz aus.txt.gz] <texte...>

Reihenfolge beim Neubauen: make_wordlist.py, make_bigrams.py, merge_alltag.py, dann dieses Skript.

  texte  Textdateien mit einem Satz pro Zeile (Common Voice) oder .conllu-Dateien
         (Universal Dependencies, dort zählen die Wörter der Tokenzeilen)
"""
import collections
import math
import re
import sys

import gzip

src, out = sys.argv[1], sys.argv[2]
files = sys.argv[3:]
bg_in = bg_out = None
if files[:1] == ["--wortfolgen"]:
    bg_in, bg_out, files = files[1], files[2], files[3:]

MIN_TOTAL = 8       # so viele Belege mitten im Satz braucht ein Wort mindestens
SWITCH = 0.90       # ab diesem Anteil wird die andere Schreibweise die Hauptform
SECOND = 0.10       # ab diesem Anteil bekommt die seltenere Schreibweise eine eigene Zeile
MIN_SECOND = 3      # und so viele Belege braucht sie mindestens

lines = []
with open(src, encoding="utf-8") as f:
    for line in f:
        line = line.rstrip("\n")
        if "\t" not in line or line.startswith("#"):
            lines.append((line, None, None))
            continue
        w, z = line.split("\t")
        lines.append((line, w, int(z)))
keys = {w.lower() for _, w, _ in lines if w}

# Nach diesen Zeichen fängt oft ein neuer Satz oder ein Zitat an: Schreibweise zählt nicht
OPENERS = set(".!?:;„“\"«»‚‘'()[]-–—…")
word_re = re.compile(r"^[^\W\d_]+(?:['\-][^\W\d_]+)*$")
tok_re = re.compile(r"[^\W\d_]+(?:['’\-][^\W\d_]+)*|[^\s]", re.UNICODE)

upper = collections.Counter()
lower = collections.Counter()
pair_forms = collections.Counter()   # (wort davor klein, folgewort genau so geschrieben)


def count(tokens):
    prev = None
    for i, t in enumerate(tokens):
        nxt = tokens[i + 1] if i + 1 < len(tokens) else None
        # "Ess- und Trinkgewohnheiten": abgeschnittener Wortteil, zählt nicht
        if bg_in and i > 0 and prev is not None and word_re.match(prev) and word_re.match(t):
            pair_forms[(prev.lower(), t)] += 1
        if i > 0 and prev not in OPENERS and nxt != "-" and word_re.match(t) and not t.isupper():
            k = t.lower()
            if k in keys:
                if t[0].isupper():
                    if t[1:] == k[1:]:
                        upper[k] += 1
                elif t == k:
                    lower[k] += 1
        prev = t


def conllu(path):
    toks = []
    with open(path, encoding="utf-8", errors="ignore") as f:
        for line in f:
            if not line.strip():
                if toks:
                    yield toks
                toks = []
                continue
            if line.startswith("#"):
                continue
            p = line.split("\t")
            if len(p) > 1 and p[0].isdigit():
                toks.append(p[1])
    if toks:
        yield toks


for path in files:
    if path.endswith(".conllu"):
        for toks in conllu(path):
            count(toks)
    else:
        with open(path, encoding="utf-8", errors="ignore") as f:
            for line in f:
                count(tok_re.findall(line.strip()))
    print(path, "fertig", file=sys.stderr, flush=True)


def cap(w):
    return w[:1].upper() + w[1:]


switched = added = 0
result = []
seen = set()
for line, w, z in lines:
    if w is None:
        result.append(line)
        continue
    k = w.lower()
    if k in seen:
        continue
    seen.add(k)
    u, lo = upper[k], lower[k]
    total = u + lo
    is_low = w[0].islower()
    # nur einfache Fälle: ganz klein (zeit) oder nur der erste Buchstabe groß (Zeit)
    if total < MIN_TOTAL or not w[:1].isalpha() or (w != k and w != cap(k)):
        result.append(line)
        continue
    other = cap(k) if is_low else k
    mine, theirs = (lo, u) if is_low else (u, lo)
    if theirs / total >= SWITCH:
        result.append(f"{other}\t{z}")
        switched += 1
        continue
    if theirs >= MIN_SECOND and mine >= MIN_SECOND and theirs / total >= SECOND:
        # beide Schreibweisen: die häufigere behält die Häufigkeit, die seltenere bekommt
        # entsprechend weniger (Zipf-Skala: Faktor 10 = 100 Punkte)
        major, minor = (w, other) if mine >= theirs else (other, w)
        zmin = z - int(round(100 * math.log10(max(mine, theirs) / min(mine, theirs))))
        result.append(f"{major}\t{z}")
        result.append(f"{minor}\t{max(1, zmin)}")
        added += 1
        continue
    result.append(line)

# nach Häufigkeit sortieren wie vorher, Kopf- und Kommentarzeilen bleiben vorn
head = [x for x in result if "\t" not in x or x.startswith("#")]
body = [x.split("\t") for x in result if "\t" in x and not x.startswith("#")]
body.sort(key=lambda p: (p[0].lower(), p[0]))
with open(out, "w", encoding="utf-8", newline="\n") as f:
    for x in head:
        if x:
            f.write(x + "\n")
    for w, z in body:
        f.write(f"{w}\t{z}\n")
print(switched, "Wörter auf die andere Schreibweise umgestellt,", added, "zweite Schreibweisen ergänzt", file=sys.stderr)

# ---------- Wortfolgen ----------
if bg_in:
    forms_of = collections.defaultdict(list)
    for w, z in body:
        forms_of[w.lower()].append(w)
    changed = 0
    opener = gzip.open if bg_in.endswith(".gz") else open
    with opener(bg_in, "rt", encoding="utf-8") as f:
        bg = f.read().split("\n")
    res = []
    for line in bg:
        if not line or line.startswith("#"):
            res.append(line)
            continue
        p = line.split("\t")
        prev = p[0]
        for j in range(1, len(p) - 1, 2):
            n = p[j]
            fs = forms_of.get(n.lower(), [])
            if not fs or (n in fs and len(fs) == 1):
                continue
            if len(fs) == 1:
                new = fs[0]                      # nur eine Schreibweise in der Liste
            else:
                cnt = {x: pair_forms[(prev, x)] for x in fs}
                best = max(cnt, key=cnt.get)
                new = best if cnt[best] > 0 else (n if n in fs else fs[0])
            if new != n:
                p[j] = new
                changed += 1
        res.append("\t".join(p))
    opener = gzip.open if bg_out.endswith(".gz") else open
    with opener(bg_out, "wt", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(res))
    print(changed, "Folgewörter in den Wortfolgen angepasst", file=sys.stderr)

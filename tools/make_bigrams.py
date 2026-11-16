"""Erzeugt die eingebauten Wortfolgen (welches Wort folgt oft auf welches) für NoHandsNeeded.

Aufruf:
    python make_bigrams.py <sprache> <wortliste.txt> <ausgabe.txt> [--extra wortfolgen_extra.txt] <texte...>

  wortliste.txt  die Wortliste der Sprache: nur Wörter daraus kommen in die Wortfolgen
  texte          Textdateien mit einem Satz pro Zeile (Common Voice) oder .conllu-Dateien
                 (Universal Dependencies, dort zählen die Zeilen "# text = ...")
                 Dateien mit "europarl" im Namen zählen nur halb (Parlamentsreden).

Ausgabe: eine Zeile pro Wort: wort<TAB>folgewort<TAB>punkte<TAB>folgewort<TAB>punkte ...
Punkte 1 bis 99 = wie oft das Folgewort nach dem Wort kommt, in Prozent.
"""
import collections
import re
import sys

lang, wl, out = sys.argv[1], sys.argv[2], sys.argv[3]
files = sys.argv[4:]
extra_file = None
if files[:1] == ["--extra"]:
    extra_file, files = files[1], files[2:]

MIN_PAIR = 3        # so oft muss eine Wortfolge mindestens vorkommen (bei wenig Text 2)
MIN_SHARE = 0.02    # und mindestens 2 % der Fälle ausmachen
PER_WORD = 6        # höchstens so viele Folgewörter pro Wort


def low(w):
    if lang == "tr":
        w = w.replace("I", "ı").replace("İ", "i")
    return w.lower()


known = set()
with open(wl, encoding="utf-8") as f:
    for line in f:
        if "\t" in line and not line.startswith("#"):
            known.add(low(line.split("\t")[0]))

# Wörter (mit Apostroph oder Bindestrich in der Mitte); alles andere trennt Wortgruppen
tok_re = re.compile(r"[^\W\d_]+(?:['’\-][^\W\d_]+)*|[^\s]", re.UNICODE)
word_re = re.compile(r"^[^\W\d_]+(?:['\-][^\W\d_]+)*$")

pairs = collections.Counter()
first = collections.Counter()
forms = collections.defaultdict(collections.Counter)       # Schreibweise des Folgeworts je Wortfolge
wforms = collections.defaultdict(collections.Counter)      # Schreibweise des Worts mitten im Satz


def sentences(path):
    with open(path, encoding="utf-8", errors="ignore") as f:
        if path.endswith(".conllu"):
            for line in f:
                if line.startswith("# text = "):
                    yield line[9:].strip()
        else:
            for line in f:
                yield line.strip()


n_sent = 0
for path in files:
    weight = 1 if "europarl" not in path.lower() else 0.5
    for s in sentences(path):
        if not s:
            continue
        n_sent += 1
        toks = [t.replace("’", "'") for t in tok_re.findall(s)]
        prev = None
        for i, t in enumerate(toks):
            if not word_re.match(t):
                prev = None          # Satzzeichen: keine Wortfolge darüber hinweg
                continue
            k = low(t)
            if k not in known:
                prev = None
                continue
            if i > 0:
                wforms[k][t] += 1
            if prev is not None:
                pairs[(prev, k)] += weight
                forms[(prev, k)][t] += 1
                first[prev] += weight
            prev = k

if n_sent < 100000:
    MIN_PAIR = 2
by_prev = collections.defaultdict(list)
for (p, n), c in pairs.items():
    if c < MIN_PAIR:
        continue
    share = c / first[p]
    if share < MIN_SHARE:
        continue
    by_prev[p].append((share, n))

# eigene Wortfolgen für Briefe und Nachrichten: ganz nach vorn
missing = []
if extra_file:
    order = collections.defaultdict(list)
    with open(extra_file, encoding="utf-8") as f:
        for line in f:
            if line.startswith("#") or "\t" not in line:
                continue
            lg, pair = line.rstrip("\n").split("\t")
            if lg != lang:
                continue
            w1, w2 = pair.split(" ")
            if low(w1) not in known or low(w2) not in known:
                missing.append(pair)
                continue
            order[low(w1)].append(w2)
    for p, nexts in order.items():
        lst = [x for x in by_prev.get(p, []) if x[1] not in {low(n) for n in nexts}]
        top = max([x[0] for x in lst], default=0)
        for j, n in enumerate(nexts):
            share = min(0.99, max(0.30, top + 0.01) + 0.01 * (len(nexts) - j))
            lst.append((share, low(n)))
            forms[(p, low(n))] = collections.Counter({n: 10 ** 9})
        by_prev[p] = lst
if missing:
    print(lang, "nicht in der Wortliste:", ", ".join(missing), file=sys.stderr)

lines = 0
with open(out, "w", encoding="utf-8", newline="\n") as f:
    for p in sorted(by_prev):
        best = sorted(by_prev[p], reverse=True)[:PER_WORD]
        parts = [p]
        for share, n in best:
            c = forms.get((p, n)) or wforms.get(n)
            form = c.most_common(1)[0][0] if c else n
            parts += [form, str(max(1, min(99, int(round(share * 100)))))]
        f.write("\t".join(parts) + "\n")
        lines += 1
print(lang, n_sent, "Sätze,", lines, "Wörter mit Folgewörtern,",
      sum(min(PER_WORD, len(v)) for v in by_prev.values()), "Wortfolgen", file=sys.stderr)

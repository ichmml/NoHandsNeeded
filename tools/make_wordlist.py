"""Erzeugt eine Wortliste (Wort<TAB>Häufigkeit) für NoHandsNeeded.

Aufruf:
    python make_wordlist.py <sprache> <ausgabe.txt> --woerterbuch PFAD/de_DE[,PFAD/de_AT]
                            [--anzahl 200000] [--alt alte_liste.txt] [--namen namen.txt]
                            [--name Deutsch] [--kopf]

  sprache       wordfreq-Kürzel, z. B. de, en, fr, it, es, nl, pl, tr
  --woerterbuch hunspell-Wörterbücher (Komma-getrennt, mit oder ohne Pfad). Ein Wort zählt nur,
                wenn eines davon es kennt.
  --anzahl      so viele Wörter höchstens (Standard 200000)
  --alt         bisherige Liste: deren Wörter bleiben auf jeden Fall drin
  --namen       Datei mit Vornamen und Nachnamen (ein Name pro Zeile). Neue Wörter, die nur
                ein Name sind, kommen nicht in die Liste.
  --kopf        Kopfzeile "#sprache<TAB>kürzel<TAB>name" schreiben (für Sprachpakete)

So entsteht die Liste:
1. Wörter und Häufigkeiten aus wordfreq (CC BY-SA 4.0), nur Wörter aus Buchstaben.
2. Jedes Wort muss hunspell kennen, die Schreibweise (groß/klein, ß) kommt von hunspell.
3. Wortformen: Für jedes Grundwort aus dem hunspell-Wörterbuch, von dem wordfreq eine Form kennt,
   werden die Formen aus den Regeln des Wörterbuchs gebildet (Mehrzahl, Fälle, Verbformen) und
   nochmal mit hunspell geprüft. Sie bekommen die Häufigkeit ihres Grundworts minus 1 Zipf.
4. Eigennamen: Neue Wörter, die hunspell nur großgeschrieben kennt (außer im Deutschen) oder die
   in der Namensliste stehen, fallen weg.
Die Häufigkeit ist der Zipf-Wert mal 100, dadurch sind alle Sprachen untereinander vergleichbar.
"""
import argparse
import os
import re
import subprocess
import sys

from wordfreq import top_n_list, zipf_frequency
from spylls.hunspell import Dictionary

ap = argparse.ArgumentParser()
ap.add_argument("lang")
ap.add_argument("out")
ap.add_argument("--woerterbuch", required=True)
ap.add_argument("--name", default="")
ap.add_argument("--anzahl", type=int, default=200000)
ap.add_argument("--alt", default="")
ap.add_argument("--namen", default="")
ap.add_argument("--formen-pro-wort", type=int, default=80)
ap.add_argument("--kopf", action="store_true")
ap.add_argument("--alle-grundwoerter", action="store_true",
                help="auch Grundwörter aus dem Wörterbuch nehmen, die wordfreq nicht kennt")
a = ap.parse_args()

LANG = a.lang
DICTS = [d if os.path.dirname(d) else "/usr/share/hunspell/" + d for d in a.woerterbuch.split(",") if d]
GERMAN = LANG == "de"

word_re = re.compile(r"^[^\W\d_]+(['’\-][^\W\d_]+)?$")
SINGLE = {"a", "i", "y", "à", "o", "e", "è", "é", "u", "w", "z"}
SINGLE |= {"ru": {"и", "в", "с", "к", "о", "у", "я", "а"}, "cs": {"v", "s", "k", "z", "o", "u", "a", "i"},
           "da": {"i", "å"}}.get(LANG, set())


def ok_shape(w):
    return (word_re.match(w) and len(w) >= 2) or w in SINGLE


def low(w):
    if LANG == "tr":
        return w.replace("I", "ı").replace("İ", "i").lower()
    return w.lower()


def cap(w):
    if LANG == "tr":
        first = {"i": "İ", "ı": "I"}.get(w[0], w[0].upper())
        return first + w[1:]
    return w[:1].upper() + w[1:]


def log(*x):
    print(LANG, *x, file=sys.stderr, flush=True)


def known(words):
    """Welche Wörter kennt mindestens eines der Wörterbücher?"""
    words = list(words)
    ok = set()
    for d in DICTS:
        for i in range(0, len(words), 200000):
            p = subprocess.run(["hunspell", "-d", d, "-G"], input="\n".join(words[i:i + 200000]),
                               capture_output=True, text=True, encoding="utf-8")
            ok |= set(p.stdout.split("\n"))
    ok.discard("")
    return ok


def variants(w):
    vs = [w]
    if GERMAN and "ss" in w:
        idx = [m.start() for m in re.finditer("ss", w)]
        for i in idx:
            vs.append(w[:i] + "ß" + w[i + 2:])
        if len(idx) > 1:
            vs.append(w.replace("ss", "ß"))
    return vs


# ---------- 1./2. wordfreq-Wörter, mit hunspell geprüft ----------
cands = [w.replace("’", "'") for w in top_n_list(LANG, 1000000)]
cands = [w for w in cands if ok_shape(w)]
log(len(cands), "wordfreq-Kandidaten")
allv = sorted({v for w in cands for v in variants(w)})
ok_lower = known(allv)
ok_cap = known([cap(v) for v in allv])

score = {}        # Schreibweise -> Punkte
for w in cands:
    form = None
    for v in variants(w):
        if v in ok_lower:
            form = v
        elif cap(v) in ok_cap:
            form = cap(v)
        if form:
            break
    if LANG == "en" and form == "i":
        form = "I"
    if not form:
        continue
    z = int(round(zipf_frequency(w, LANG) * 100))
    if form not in score:
        score[form] = z
log(len(score), "geprüfte wordfreq-Wörter")

# gleiche Wörter nur einmal (die häufigere Schreibweise gewinnt)
by_key = {}
for f, z in score.items():
    k = low(f)
    if k not in by_key or z > score[by_key[k]]:
        by_key[k] = f
attested = {f: score[f] for f in by_key.values()}
att_keys = {low(f): z for f, z in attested.items()}

# ---------- 3. Wortformen aus den hunspell-Regeln ----------
names = set()
if a.namen:
    with open(a.namen, encoding="utf-8") as f:
        names = {low(x.strip()) for x in f if x.strip()}

gen = {}  # Form -> Punkte
dic_stems = set()
for dpath in DICTS:
    d = Dictionary.from_files(dpath)
    dic_stems |= {wd.stem for wd in d.dic.words}
    aff = d.aff
    SFX, PFX = aff.SFX, aff.PFX
    bad_flags = {x for x in (aff.NEEDAFFIX, aff.ONLYINCOMPOUND, aff.FORBIDDENWORD, aff.KEEPCASE) if x}

    def sfx(stem, flags):
        out = []
        for fl in flags:
            for s in SFX.get(fl, ()):
                if s.strip and not stem.endswith(s.strip):
                    continue
                if not s.cond_regexp.search(stem):
                    continue
                out.append((stem[:len(stem) - len(s.strip)] + s.add if s.strip else stem + s.add, s))
        return out

    def pfx(stem, flags):
        out = []
        for fl in flags:
            for p in PFX.get(fl, ()):
                if p.strip and not stem.startswith(p.strip):
                    continue
                if not p.cond_regexp.search(stem):
                    continue
                out.append((p.add + stem[len(p.strip):], p))
        return out

    for wd in d.dic.words:
        stem = wd.stem
        if not stem or not ok_shape(stem):
            continue
        if not GERMAN and stem[:1].isupper():
            continue          # Eigenname
        if stem[:1].isupper() and low(stem) in names and low(stem) not in att_keys:
            continue
        forms = {stem: 0}
        for f1, s1 in sfx(stem, wd.flags):
            forms.setdefault(f1, 0)
            if s1.flags:
                for f2, _ in sfx(f1, s1.flags):
                    forms.setdefault(f2, 30)
            if s1.crossproduct:
                for f3, p in pfx(f1, wd.flags):
                    if p.crossproduct:
                        forms.setdefault(f3, 30)
        for f1, p1 in pfx(stem, wd.flags):
            forms.setdefault(f1, 0)
        forms = {f: v for f, v in forms.items() if ok_shape(f)}
        if LANG == "en":
            # gebildete Genitive (frontier's) bringen nichts, belegte bleiben über wordfreq drin
            forms = {f: v for f, v in forms.items() if not f.endswith("'s")}
        zs = [att_keys[low(f)] for f in forms if low(f) in att_keys]
        if zs:
            # Formen echter Wörter: unter den belegten Wörtern einsortieren
            base = max(100, min(max(zs) - 150, 300))
        elif a.alle_grundwoerter:
            base = 90     # Wort aus dem Wörterbuch, in wordfreq nicht belegt: ganz hinten
        else:
            continue
        extra = sorted((f for f in forms if low(f) not in att_keys), key=lambda f: (forms[f], len(f)))[:a.formen_pro_wort]
        for f in extra:
            z = base - forms[f] - (0 if f == stem else 5)
            if z < 50:
                z = 50
            if gen.get(f, 0) < z:
                gen[f] = z
    log(dpath, len(gen), "neue Formen bisher")

okgen = known(gen.keys())
gen = {f: z for f, z in gen.items() if f in okgen}
log(len(gen), "neue Formen nach Prüfung")

# ---------- 4. alte Liste, Namen, zusammenführen ----------
old = {}
if a.alt:
    with open(a.alt, encoding="utf-8") as f:
        for line in f:
            if line.startswith("#") or "\t" not in line:
                continue
            w, z = line.rstrip("\n").split("\t")
            old[w] = int(z)
old_keys = {low(w) for w in old}

def is_name(w):
    # Personenname: steht in der Namensliste und ist kein normales Wort aus dem Wörterbuch
    return w[:1].isupper() and low(w) in names and (not GERMAN or w not in dic_stems)


final = {}
dropped = 0
for w, z in old.items():
    if not GERMAN and len(w) <= 2 and w[:1].isupper() and w not in ("I", "Mr", "Ms", "Dr"):
        dropped += 1
        continue          # Kürzel wie Th, Va, Mn (Abkürzungen, chemische Zeichen)
    if z < 400 and is_name(w):
        dropped += 1
        continue          # seltener Personenname aus der alten Liste
    final[low(w)] = (w, z)
log(dropped, "seltene Namen und Kürzel aus der alten Liste entfernt")


def put(w, z):
    k = low(w)
    if k in old_keys:
        return
    if is_name(w):
        return
    if not GERMAN and w[:1].isupper() and w not in ("I",):
        return            # hunspell kennt das Wort nur großgeschrieben: Eigenname
    if k not in final or z > final[k][1]:
        final[k] = (w, z)


for w, z in attested.items():
    put(w, z)
for w, z in gen.items():
    put(w, z)

items = sorted(final.values(), key=lambda x: -x[1])
must = [x for x in items if low(x[0]) in old_keys]
rest = [x for x in items if low(x[0]) not in old_keys]
rest.sort(key=lambda x: (-x[1], len(x[0])))
items = must + rest[:max(0, a.anzahl - len(must))]
items.sort(key=lambda x: -x[1])

with open(a.out, "w", encoding="utf-8", newline="\n") as f:
    if a.kopf:
        f.write(f"#sprache\t{LANG}\t{a.name or LANG.upper()}\n")
    for w, z in items:
        f.write(f"{w}\t{z}\n")
log(len(items), "Wörter geschrieben,", len(must), "davon aus der alten Liste")

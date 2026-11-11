"""Erzeugt eine Wortliste (Wort<TAB>Häufigkeit) für NoHandsNeeded.

Aufruf:
    python make_wordlist.py <sprache> <ausgabe.txt> [--woerterbuch de_DE,de_AT] [--name Deutsch]
                            [--anzahl 80000] [--kopf]

  sprache       wordfreq-Kürzel, z. B. de, en, fr, it, es, nl, pl, tr
  --woerterbuch hunspell-Wörterbücher zum Prüfen (Komma-getrennt). Ein Wort zählt, wenn
                eines davon es kennt. Ohne Angabe wird nicht geprüft (nur Buchstaben-Filter).
  --name        Anzeigename, wird mit --kopf in die erste Zeile geschrieben
  --kopf        Kopfzeile "#sprache<TAB>kürzel<TAB>name" schreiben (für Sprachpakete zum Download)

Häufigkeiten: wordfreq (Daten CC BY-SA 4.0). Schreibweise: hunspell.
Die Häufigkeit ist der Zipf-Wert mal 100, dadurch sind alle Sprachen untereinander vergleichbar.
"""
import argparse
import re
import subprocess

from wordfreq import top_n_list, zipf_frequency

ap = argparse.ArgumentParser()
ap.add_argument("lang")
ap.add_argument("out")
ap.add_argument("--woerterbuch", default="")
ap.add_argument("--name", default="")
ap.add_argument("--anzahl", type=int, default=80000)
ap.add_argument("--kopf", action="store_true")
a = ap.parse_args()

LANG = a.lang
DICTS = [d for d in a.woerterbuch.split(",") if d]

# Buchstaben, optional mit einem Apostroph oder Bindestrich in der Mitte (don't, aujourd'hui, e-mail)
word_re = re.compile(r"^[^\W\d_]+(['’\-][^\W\d_]+)?$")
cands = [w.replace("’", "'") for w in top_n_list(LANG, a.anzahl)]
SINGLE = {"a", "i", "y", "à", "o", "e", "è", "é", "u", "w", "z"}
SINGLE |= {"ru": {"и", "в", "с", "к", "о", "у", "я", "а"}, "cs": {"v", "s", "k", "z", "o", "u", "a", "i"},
           "da": {"i", "å"}}.get(LANG, set())
cands = [w for w in cands if word_re.match(w) and len(w) >= 2 or w in SINGLE]


def cap(w):
    if LANG == "tr":
        first = {"i": "İ", "ı": "I"}.get(w[0], w[0].upper())
        return first + w[1:]
    return w[:1].upper() + w[1:]


def variants(w):
    vs = [w]
    if LANG == "de" and "ss" in w:
        # wordfreq faltet ß zu ss, hier wieder zurück probieren
        idx = [m.start() for m in re.finditer("ss", w)]
        for i in idx:
            vs.append(w[:i] + "ß" + w[i + 2:])
        if len(idx) > 1:
            vs.append(w.replace("ss", "ß"))
    return vs


def known(words):
    if not DICTS:
        return set(words)
    ok = set()
    for d in DICTS:
        p = subprocess.run(["hunspell", "-d", d, "-G"], input="\n".join(words),
                           capture_output=True, text=True, encoding="utf-8")
        ok |= set(p.stdout.split("\n"))
    return ok


allv = sorted({v for w in cands for v in variants(w)})
ok_lower = known(allv)
ok_cap = known([cap(v) for v in allv])

out, seen = [], set()
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
    if not form or form.lower() in seen:
        continue
    seen.add(form.lower())
    out.append((form, int(round(zipf_frequency(w, LANG) * 100))))

with open(a.out, "w", encoding="utf-8", newline="\n") as f:
    if a.kopf:
        f.write(f"#sprache\t{LANG}\t{a.name or LANG.upper()}\n")
    for w, z in out:
        f.write(f"{w}\t{z}\n")
print(LANG, len(cands), "Kandidaten,", len(out), "Wörter")

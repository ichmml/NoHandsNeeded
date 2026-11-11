# Lizenz der Wortlisten

Betrifft:
- `Resources/wortliste_de.txt` und `Resources/wortliste_en.txt` (in `NoHandsNeeded.exe` eingebaut)
- alle Dateien im Ordner `sprachpakete/`

## Herkunft

Die Wörter und ihre Häufigkeiten stammen aus **wordfreq** von Robyn Speer:
https://github.com/rspeer/wordfreq

Die Daten von wordfreq stehen unter der Lizenz
**Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0)**:
https://creativecommons.org/licenses/by-sa/4.0/

## Bearbeitung

Die Listen wurden mit `tools/make_wordlist.py` erzeugt. Dabei wurden
- nur Wörter aus Buchstaben behalten,
- die Schreibweise (Groß-/Kleinschreibung, im Deutschen ß) mit hunspell-Wörterbüchern geprüft,
- die Häufigkeit als Zipf-Wert × 100 gespeichert.

Die hunspell-Wörterbücher dienen nur zum Prüfen und sind nicht in den Listen enthalten.

## Lizenz der bearbeiteten Listen

Die bearbeiteten Wortlisten stehen wegen der ShareAlike-Bedingung ebenfalls unter
**CC BY-SA 4.0**. Wer sie weitergibt oder verändert, muss die Herkunft (wordfreq, Robyn Speer)
nennen und Änderungen unter derselben Lizenz weitergeben.

# Lizenz der Wortlisten und Wortfolgen

Betrifft die Dateien, die in `NoHandsNeeded.exe` eingebaut sind:
- `Resources/wortliste_xx.txt.gz` (Wörter mit Häufigkeit, eine Datei pro Sprache)
- `Resources/wortfolgen_xx.txt.gz` (welche Wörter oft aufeinander folgen)
- außerdem die älteren Listen im Ordner `sprachpakete/`

xx steht für de, en, nl, fr, da, pl, cs, it, es, tr, ru.

## Wortlisten (ab Version 1.03)

### Herkunft

1. **wordfreq** von Robyn Speer: Wörter und Häufigkeiten.
   https://github.com/rspeer/wordfreq
   Lizenz der Daten: **CC BY-SA 4.0**, https://creativecommons.org/licenses/by-sa/4.0/
2. **Hunspell-Wörterbücher**: prüfen, ob ein Wort echt ist, liefern die richtige Schreibweise
   und die Wortformen (Mehrzahl, Fälle, Verbformen). Die Wörterbücher selbst sind nicht
   in den Listen enthalten, nur einzelne Wörter, die damit gebildet und geprüft wurden.

   | Sprache | Wörterbuch | Lizenz (laut Paket) |
   |---|---|---|
   | de | de_DE (igerman98, Björn Jacke) | GPL 2 oder neuer / LGPL 2.1 |
   | en | en_US, en_GB (SCOWL) | SCOWL-Lizenz (frei, BSD-ähnlich) |
   | nl | OpenTaal | BSD oder CC BY 3.0 |
   | fr | Grammalecte / dicollecte | MPL 2.0 |
   | da | Stavekontrolden (LibreOffice) | GPL 2 / LGPL 2.1 / MPL 1.1 |
   | pl | sjp.pl | GPL / LGPL / MPL / Apache 2.0 / CC BY 4.0 |
   | cs | cs_CZ (LibreOffice) | GPL 2 |
   | it | it_IT (LibreOffice) | GPL 3 |
   | es | RLA-ES | GPL 3 / LGPL 3 / MPL 1.1 |
   | tr | tr_TR (hunspell-tr) | MPL 2.0 / LGPL / GPL |
   | ru | ru_RU (LibreOffice, Alexander Lebedev) | BSD-ähnlich |

   Ein Wörterbuch dient hier als Prüfwerkzeug. Einzelne Wörter einer Sprache sind keine
   geschützten Werke, das Wörterbuch selbst wird nicht weitergegeben.
3. **Namenslisten** (nur zum Aussortieren von Personennamen, nicht enthalten):
   firstname-database (Matthias Winkelmann) und die US-Nachnamenliste von FiveThirtyEight.

### Bearbeitung

Erzeugt mit `tools/make_wordlist.py`:
- nur Wörter aus Buchstaben (mit Apostroph oder Bindestrich in der Mitte),
- jedes Wort muss ein Hunspell-Wörterbuch kennen,
- Wortformen aus den Regeln des Wörterbuchs gebildet und nochmal mit Hunspell geprüft,
  sie stehen hinter den belegten Wörtern,
- keine Personennamen: neue Wörter, die nur großgeschrieben gültig sind (außer im Deutschen)
  oder in den Namenslisten stehen, fallen weg, seltene Namen und Kürzel aus den alten Listen auch,
- Häufigkeit als Zipf-Wert × 100.

Etwa 180.000 bis 200.000 Wörter pro Sprache.

## Wortfolgen (ab Version 1.03)

### Herkunft

1. **Common Voice** (Mozilla): Sätze aus `server/data/xx/`, alle 11 Sprachen.
   https://github.com/common-voice/common-voice
   Lizenz der Sätze: **CC0 1.0** (gemeinfrei), siehe `server/data/LICENSE`.
2. **Universal Dependencies** (nur der Satztext, Zeilen `# text = …`):

   | Sprache | Baumbanken | Lizenz |
   |---|---|---|
   | de | UD_German-HDT, UD_German-GSD | CC BY-SA 4.0 |
   | en | UD_English-EWT | CC BY-SA 4.0 |
   | nl | UD_Dutch-Alpino, UD_Dutch-LassySmall | CC BY-SA 4.0 |
   | fr | UD_French-GSD | CC BY-SA 4.0 |
   | da | UD_Danish-DDT | CC BY-SA 4.0 (oder GPL 2) |
   | pl | UD_Polish-PUD | CC BY-SA 4.0 |
   | cs | UD_Czech-CAC, UD_Czech-CLTT, UD_Czech-PUD | CC BY-SA 4.0 |
   | it | UD_Italian-MarkIT (CC BY 4.0), UD_Italian-PUD (CC BY-SA 3.0) | |
   | es | UD_Spanish-AnCora (CC BY 4.0), UD_Spanish-GSD (CC BY-SA 4.0) | |
   | tr | UD_Turkish-BOUN, UD_Turkish-Kenet, UD_Turkish-Penn | CC BY-SA 4.0 |
   | ru | UD_Russian-Taiga, UD_Russian-GSD | CC BY-SA 4.0 |

   https://github.com/UniversalDependencies
   Baumbanken mit NC-Lizenz (nicht kommerziell) oder GPL wurden bewusst nicht benutzt.
3. **Eigene Ergänzungen**: `tools/wortfolgen_extra.txt`, typische Wortfolgen für Briefe,
   Mails und Nachrichten ("mit freundlichen Grüßen", "thank you"), selbst zusammengestellt.

### Bearbeitung

Erzeugt mit `tools/make_bigrams.py`: Wortpaare innerhalb eines Satzteils gezählt (nicht über
Satzzeichen hinweg), nur Wörter aus der Wortliste, mindestens 2 bis 3 Vorkommen und 2 % Anteil,
höchstens 6 Folgewörter pro Wort. Gespeichert wird nur die Statistik (Wort, Folgewort,
Anteil in Prozent), keine Sätze.

## Lizenz der fertigen Listen

Wegen der ShareAlike-Bedingungen (wordfreq, Universal Dependencies) stehen die Wortlisten und
Wortfolgen unter **CC BY-SA 4.0**. Wer sie weitergibt oder verändert, nennt die Herkunft
(wordfreq von Robyn Speer, Common Voice, Universal Dependencies) und gibt Änderungen unter
derselben Lizenz weiter.

Der Programmcode steht unabhängig davon unter der MIT-Lizenz (siehe `LICENSE`).

NoHandsNeeded
=============
On-screen keyboard, Version 1.01

Why NoHandsNeeded? Because that is exactly what this keyboard is built for: using the PC
without your hands, for example with a mouth joystick like the Quadstick, a head mouse or
your eyes.

This manual was translated from German by an AI. Corrections from native speakers are
welcome on GitHub: https://github.com/ichmml/nohandsneeded (folder Anleitung).

An on-screen keyboard for Windows, built for people who use the PC without their hands,
for example with a mouth controller (Quadstick), a head mouse or eye tracking.
Goal: as few clicks as possible. It never takes the focus away from the program you type in.

Start
-----
Put NoHandsNeeded.exe in its own folder (e.g. C:\Tools\NoHandsNeeded) and run it.
Nothing to install, .NET Framework 4.8 is already part of Windows 10/11.
To update, exit the old version via the tray icon and simply replace the .exe.
Settings are kept.

On first start Windows may show "Windows protected your PC". Click "More info" and
"Run anyway". This happens because the file is not signed.

On first start the keyboard adapts to the Windows language (suggestions, key layout and
interface) and shows a short introduction to click through. You can show it again any time:
Settings → Other → "Show introduction again", or the tray menu.
This manual opens via "Open manual" at the very top of the settings or in the tray menu,
always in the interface language.

Next to the .exe a folder "NoHandsNeeded-Daten" is created with einstellungen.txt, makros.yaml,
protokoll.txt (log), the folder "Lernen" (learned words) and tresor.dat (passwords, encrypted).
If that folder is not writable, the data goes to %APPDATA%\NoHandsNeeded.

Top bar
-------
  Esc      Escape key (Fn is at the bottom left of the bottom row)
  middle   at the far left in red the word you are typing (see below), next to it the
           word suggestions, one click inserts the word with a space
  Macros   opens the macro bar (see below)
  EN       language of the suggestions, one click switches (see "Languages")
  ⚿        show/hide the password vault (see "Password vault")
  ◷        dwell click on/off
  ⚙        settings
  ▾        collapse to a small round button (click or dwell opens it again)
  ✕        close the keyboard: tap twice (after the first tap ✕ turns red)

Typed word (top left, red)
--------------------------
The first slot of the suggestion bar shows in bold red what you are typing, letter by
letter. Numbers are shown completely including dots, a waiting accent key (e.g. ´) appears
at the end. Long words are never shortened, the slot simply gets wider.
Special characters keep the display: it shows everything since the last space, Enter or
Tab, e.g. "s!add" or a web address. The suggestions next to it belong to the last word
("add"). Clicking such a red slot only adds a space.
  - If the keyboard does not know the word yet, a small "new" appears next to it. Clicking
    the red word inserts it with a space and remembers it right away, from then on it is
    suggested.
  - In a password field you see red dots and the number of characters, with 👁 next to it.
    👁 shows the password in plain text for 10 seconds (or until the next click). During that
    time the keyboard is excluded from screenshots and screen sharing. Passwords from the
    vault only appear as dots. After Enter or when you leave the field, everything is gone.
  - In the keyboard's own windows (new vault entry, backup password) the password is shown
    in plain text while you type, in the window and at the top of the keyboard.
    "Show / hide" switches to dots. Nothing is learned, logged or recorded there.
  - Only what was typed with this keyboard is shown. After a click into the window or a
    real key press the slot is empty.
  - Settings → Word suggestions: on/off, "dots with 👁" or "always dots only", time until
    the plain text is hidden.

Moving and size
---------------
Moving:
  - Press and drag ✥ at the bottom right, or dwell on it briefly.
  - Hold the mouse button down on any key a bit longer (default 0.5 seconds). An orange
    ring fills up, then the keyboard sticks to the mouse. Releasing drops it. The key is not
    typed. A short click types as usual (on release).
  - With dwell click: dwell on a key clearly longer (default twice the dwell time). First
    the key is typed once, then the orange ring fills and the keyboard follows the mouse.
    Holding the mouse still for a moment or clicking drops it. Backspace, Delete and the
    arrows repeat while dwelling, so they cannot be used to drag.
  - Esc or right-click cancels, the keyboard jumps back.
  - The keyboard always stays fully on screen, also with several monitors.
Size: drag any edge or the blue corner at the bottom left with the mouse.
  The size is a percentage of the screen (default 60 % wide, 31 % high, bottom right).
  Settings → "Moving and size": sliders for width and height, default position
  (bottom right, centre or left) and "Reset size and position".
Several screens: the keyboard can be dragged to any screen, also when the screens have
  different scaling (e.g. 125 % and 100 %). When dropped it takes the same percentage size
  there. If a screen is missing at start, it appears at the bottom right of the main screen.
  The same applies to the round button.
In the settings the suggestion bar can also have two rows, with its own font size.

Keys
----
Fn (bottom left): second layer with F1 to F12, Home, End, Page up/down, special characters.
Esc is at the top left.
Shift, Ctrl, Alt, Win, AltGr:
  tap once   = applies to the next key (blue frame)
  tap twice = off again. There is no locking any more. For selecting, Shift stays on
  across several arrow keys until another key comes.
Win works differently: tap once for the next key (e.g. Win+E for Explorer), tap twice to
press the Windows key on its own and open the Start menu.

Bottom row: Fn, Ctrl, Win, Alt, space, AltGr (right next to the space bar), abc/ABC, four
special characters, arrows, ✥. The four special characters are ! ? : - by default and can
be changed in the settings under "Special characters at the bottom" (one character per field,
optionally a second one for Shift, e.g. ".:"). After a word suggestion a punctuation mark
moves up to the word and the space goes behind it ("house ." becomes "house. ").
Otherwise too, a space is added automatically after . , ! ? : ; except in numbers (1,5 or
10:30), web addresses, e-mail addresses and in e-mail or IBAN fields. If you type a space
yourself, it is not doubled. Can be switched off in the settings.

Case key (next to the space bar): changes the case of the last word.
    "abc" with arrow up    = word is lower case, one press capitalizes the first letter
    "ABC" with arrow down  = word has capitals, one press makes it lower case
    Afterwards it shows the other option, so you can switch back and forth.
    Also works right after a word suggestion or after a space or full stop.
    Grey "Aa" = no word known. The key only knows words typed with this keyboard and
    forgets them after a click into the window, a window switch or Enter. It does not know
    words from voice input (e.g. Wispr Flow) or another keyboard.
    ALL CAPS: use the Caps Lock key ⇪.

At the start of a sentence (after . ! ? and a space, or after Enter) the next letter is
capitalized automatically. Can be turned off in the settings.

Real key presses: if you press a key on the Quadstick (or a normal keyboard), it lights up
red on the on-screen keyboard while it is held and for 1.5 seconds after. So you can see
Ctrl+Shift for Wispr Flow as long as you hold it. What the on-screen keyboard types itself
does not light up. Keys are only shown, never stored or logged.
After a real key press the keyboard forgets the word you started, because pasted or
dictated text may be there now. Shift or Ctrl alone change nothing, Ctrl+Shift does.
The shortcut keys (Pause, numpad -) do not count.
Settings → Operation: on/off and how long keys keep glowing.

Own windows (vault, password entry, PIN, macros, export/import): the keyboard stays
clickable, so you can type in these windows as usual. The windows open where the keyboard
does not cover them.

Word per program: when you switch windows (e.g. from the mail program to the browser and
back), the keyboard remembers for each window the started word, the word before it and the
sentence start. Back in the window you simply keep writing.
  - Per window: two browser windows are separate, tabs in the same window are not.
  - If you bring the window back with a click into the text, it also continues. If you click
    into the middle of the text, the suggestion will not fit. Turn off: Settings →
    Operation → "Continue writing after a click into the window".
  - At most 30 windows, at most 30 minutes, only in memory. Never password fields.

With dwell click, Backspace, Delete and the arrows repeat as long as you stay on them.
All other keys only trigger again after you have left the key once.

Macros
------
Tap "Macros": the macros appear at the top instead of the word suggestions.
  - One click runs the macro, then the word suggestions come back.
  - Groups appear as "Name ›", one click opens them, "‹ Name" goes back.
  - Macros that match the active program come first.
  - If not all fit, "more ›" pages on.
  - "✕ Back" closes the macro bar.

Right of the macro bar:
  ●  Record: simply type on the keyboard as usual, everything is recorded
     (text, Enter, shortcuts like Ctrl+S). The keyboard gets a red frame.
     "■ Stop" ends the recording, then confirm the name and you are done.
     ✕ discards the recording.
  +  Quick save: what you typed last (since the last click into the window) is saved as a
     text snippet. A name is already suggested, one click is enough.
  ✎  open the macro editor

Macro editor (✎ or Settings → Edit macros):
  New, Edit, Copy, Up/Down, Delete. In the macro itself: name, group, colour, program,
  global shortcut and any number of steps:
    Text     placeholders {date} {time} {weekday} {cursor}
             (German also works: {datum} {uhrzeit} {wochentag})
             ({cursor} = the cursor is placed there after inserting)
    Keys     e.g. Ctrl+A, Ctrl+C, Enter   or   Alt+Tab   or   Win+D
             "Record key" enters a pressed key directly
    Open     program, file, folder or web page
    Wait     pause in milliseconds

The macros are stored in NoHandsNeeded-Daten\makros.yaml and can also be edited by hand
(a description is at the top of the file). Keywords work in English (name, group, steps,
keys, open, wait) or German (name, gruppe, schritte, tasten, oeffnen, warten).
Then click "Reload file" in the editor. If the file has an error, the keyboard shows the line
instead of crashing. When saving, the previous version is kept as makros.yaml.bak.

Macros type by keyboard simulation, never through the clipboard. They do not feed into the
word suggestions. Passwords do not belong in macros, that is what the vault is for.

Languages
---------
Built in, no internet needed: German, English, Dutch, French, Danish, Polish, Czech,
Italian, Spanish, Turkish and Russian.
The language button at the top cycles through the enabled languages, plus mixed
(e.g. DE+EN, by frequency). Mixed can be turned off.

On the very first start the keyboard takes the Windows language plus English. If Windows is
set to Dutch, it starts with Dutch and the Dutch layout.

Suggestions without accents: "zolw" brings "żółw", "cafe" brings "café", "nino" brings
"niño". So you do not have to search for special characters. This also applies to learned
words.

Key layout: follows the language (with mixed, the first language).
  German QWERTZ, English and Dutch QWERTY, French AZERTY, Danish with æ ø å, Polish with
  ą ć ę ł ń ó ś ź ż via AltGr, Czech with ě š č ř ž ý á í é in the number row, Italian,
  Spanish with ñ ¿ ¡, Turkish Q with ı ğ ü ş ö ç, Russian ЙЦУКЕН.
  Russian has an "ABC/АБВ" key for Latin letters (mail addresses, links).
  Accent keys work like in Windows (dead keys): in French, Spanish, Danish, Czech and Dutch
  press ´ ` ^ ¨ first, then the letter (´ + e = é). With the space bar afterwards you get the
  character alone. German needs no dead keys.
  The keyboard types Unicode, so the Windows layout does not have to be changed.
  Settings → Languages → Key layout: matching the language, always German or always English.

Settings → Languages:
  - A tick enables a language, ▲/▼ changes the order when switching.
  - "Remember language per program": if you switch the language in a program, the keyboard
    remembers it and switches automatically next time you are in that program. For example
    Outlook English, WhatsApp German.
  - "Edit custom terms": names and technical terms that are suggested in all languages.
    File: NoHandsNeeded-Daten\eigene_woerter.txt
  - "Add from file": use a word list without internet (word, tab, frequency; file name =
    language code, e.g. pt.txt).
  - "Remove": deletes a language you added yourself (built-in ones can only be turned off).
Languages you added yourself are stored in NoHandsNeeded-Daten\Sprachen.
Optionally there is a global shortcut "Switch language".

Interface language
------------------
Settings, menus, messages, key labels, the introduction and this manual are available in
German, English, Dutch, French, Danish, Polish, Czech, Italian, Spanish, Turkish and
Russian. Default is the Windows language, otherwise English.
Change it: Settings → Other → "Interface language" → Apply.
The translations were made by an AI. Corrections from native speakers are welcome on GitHub:
https://github.com/ichmml/nohandsneeded (interface: folder Resources/Texte, manual:
folder Anleitung). If a text is missing in a language, it appears in English.

Learning
--------
The keyboard learns while you write:
  - new words (names, technical terms). They are suggested once you have written them
    twice (adjustable from 1 to 5).
  - how often you use which word. Frequent words move to the front.
  - which word often follows which. After a space, fitting next words appear at the top
    (e.g. after "my" the word you usually write next).
  - your spelling: if you turn "iphone" into "iPhone" with the case key, you get "iPhone"
    suggested next time.
If you delete a word right away with ⌫, it is forgotten again.

Numbers: dates (09/10/1991), phone numbers (+1 212 555 0100) and postcodes before a place
name are learned as well, as are long numbers from 9 digits such as customer or contract
numbers. They are suggested right after the first time. When you type digits, matching
numbers appear at the top. Separators do not count, so "0910" brings "09/10/1991". The
number is inserted in the learned spelling.
PINs (4 to 8 digits without separators) and card numbers are learned too, but encrypted in a
separate file (zahlen_geheim.dat). They only go into a backup with a password. Can be
switched off in the learning settings ("Learn PINs and card numbers (encrypted)"). Never
learned: single numbers and amounts, times, anything in password fields and nothing that
looks like a password (digits mixed with letters, very long words). PINs and card numbers
are never taken from your own texts.
IBAN: a valid IBAN (correct check digit) is remembered, typed with or without spaces. Next
time the country code and the first digit ("DE8") are enough: it appears in green with 🏦 at
the top and one click types all of it. In fields called IBAN and after "IBAN:" it shows up
right away. Can be switched off in the learning settings.
Numbers apply to all languages, file: NoHandsNeeded-Daten\Lernen\zahlen.txt. Can be turned off
under Settings → Learning, view and delete under "Learned words" → choose "Numbers".

Settings → Learning:
  - "Learn from my texts": choose a folder (e.g. notes or documents). It reads .txt, .md,
    .docx, .html, .eml and .mbox, also in subfolders. Folders starting with a dot (.obsidian,
    .git) are skipped.
    Only statistics are stored, which words occur how often and in which order, never the
    texts. Lines with password, PIN, IBAN, login, token and the like are skipped, as are
    links, mail addresses, code blocks and quoted mails.
    With several languages, each paragraph goes to the language it fits.
  - "View and delete learned words": search, delete single words, forget everything.
  - Tray icon → "Pause learning", e.g. when somebody else is typing.
Learned data is stored in NoHandsNeeded-Daten\Lernen\en.txt (one file per language) and saved
every 2 minutes and on exit.

Password vault
--------------
So that you do not have to type long passwords character by character every time.
  - When you click into a password field, your saved passwords appear at the top instead of
    the word suggestions (yellow, with ⚿). The one matching the window comes first. One click
    types the password into the field. Then Enter.
  - If you typed a password yourself, the keyboard asks after Enter or when you leave the
    field: "Save in vault?". "Save" opens a window with a suggested name, which you confirm.
    "No" or simply carrying on discards it.
  - "+ Add password" opens a window for a new entry. The name is already suggested from the
    web page or program, the password is shown in plain text while you type, "Create a secure
    password" fills it in if you like. "Save" stores it and it appears in the bar right away.
  - If not all passwords fit into the bar, "more ›" pages on, the ones matching the open
    page come first. "🔍 Search": type a few letters and the bar only shows matching entries
    (name, user or program). The letters do not go into the program. Backspace deletes, Esc,
    Enter or "✕ End search" ends the search.
  - If you have just typed a password on a page, it is already filled in, and so is an
    e-mail address you typed shortly before in the same window. The cursor goes straight to
    the next empty field. In name and e-mail you type normally, nothing is learned there.
  - "🎲 Create and type" (only in a password field) creates a secure
    password (default 16 characters), types it into the field and offers saving right away.
    Be sure to save it, otherwise it is gone.
  - In the vault window the password is always shown in plain text, also when you edit an
    existing entry, so you can see whether you type it correctly. "Show / hide" hides it if
    needed.
  - ⚿ at the top right shows the vault also outside password fields, "✕ Hide" hides it.
Security:
  - tresor.dat is encrypted with Windows (DPAPI) and can only be read with your Windows
    account on this PC. Copied elsewhere, the file is useless. To transfer it, use "Back up
    and transfer to another PC" (see below).
  - Optionally a PIN (Settings → Password vault). Then the vault is locked after each start
    and locks itself again after 15 minutes without use (adjustable). You enter the PIN with
    large number keys. Without the PIN nobody gets to the passwords, not even you. So
    remember it well.
  - Passwords never appear in the normal word suggestions, are never learned, never taken
    into macro recordings and never inserted through the clipboard.
  - The keyboard recognizes password fields by the Windows flag for protected fields. This
    works in Windows programs and the common browsers. If a field is not recognized, ⚿ shows
    the vault.
Edit: Settings → Password vault → "Edit vault" (or tray icon).
There you set name, user name, password (with a show button) and keywords for the window
title ("gmail, google"), so that the right password comes first.

E-mail addresses: when you type an e-mail address, the keyboard remembers it like a new word
(not in password fields). From then on it appears at the top in blue with ✉ as soon as you
have typed the first 3 letters, one click inserts the whole address. If you click into a field
for e-mail or user name, the addresses appear at the top right away, without typing a letter.
The keyboard recognizes such a field by its label, which works on most login pages.
Turn off: Settings → Learning. View and delete: "Learned words" → "E-mail addresses".

Global shortcuts and Quadstick
------------------------------
These shortcuts also work when another program is in front:
  Pause            show/hide the keyboard
  numpad -         open the macro bar (called "NumMinus" in the keyboard)
In addition every macro can get its own shortcut (in the macro editor).
In the settings each shortcut shows whether it is active (green tick) or not (red, with
the reason).

If you open the keyboard with the shortcut, it appears where the mouse pointer is: centred
just below it so the spot stays visible, above it if there is not enough room. From the
taskbar, the tray or the round button it stays in its usual place. Can be switched off in
the settings ("Open where the mouse is when using the shortcut (e.g. Pause)").
Below that you choose where exactly: below or above the mouse, centred, to the right or to
the left. If it does not fit on the chosen side, it uses the other one.

Change: Settings → Global shortcuts → "Record". A window opens, then press the key on the
Quadstick. The detected key is shown large in the window, confirm with "Apply". Or click
"From list".

If a shortcut does not work: Settings → "Test shortcuts" and press the Quadstick key.
  - Nothing appears: the key does not reach the PC at all. The Quadstick does not send it,
    e.g. because it cannot send F13 to F24. Use another key, e.g. Pause or an operator key
    from the numpad.
  - "Key received" appears but no "Shortcut triggered": the key arrives, but the shortcut is
    not active or set differently. Check the red message in the settings.
  - Both appear: everything is fine.
protokoll.txt (Settings → "Open log") shows which shortcuts were registered, which were
not (with the Windows error code) and which were triggered.

Assigning a Quadstick key:
  In the Quadstick configuration sheet (Google spreadsheet) one line per shortcut, example:
      kb_pause          normal    <input>
      kb_keypad_minus   normal    <input>
  1. Choose the input you want in the yellow column (e.g. mp_triple_puff).
  2. Load the configuration onto the Quadstick as usual.
  3. Check with "Test shortcuts".
  The Quadstick cannot send F13 to F24 and Scroll Lock. Pause and the numpad operator keys
  work well (kb_keypad_slash, kb_keypad_asterisk, kb_keypad_minus, kb_keypad_plus). Do not
  use kb_keypad_enter (treated like Enter) or the numpad digits (depend on Num Lock).

Tray icon: left click shows/hides the keyboard. Right click: edit macros, pause learning,
edit password vault, settings, open manual, show introduction, donate, exit.

Taskbar: while the keyboard is running, it has its own button with the keyboard icon in the
taskbar. One click always opens the keyboard fully, also when it is hidden or collapsed to
the round button (an open keyboard stays open), afterwards the focus is right back in the
program you were writing in. Turn off: Settings → Start.

Back up and transfer to a second PC
-----------------------------------
Settings → "Back up and transfer to another PC".
  - "Export data …": tick what should be included (learned words, word sequences and
    numbers, custom terms, macros, passwords from the vault). Then choose a folder. A file
    like "NoHandsNeeded 2026-11-08.nohands" is created.
  - If passwords are included, the backup needs its own password (at least 8 characters).
    The file is then encrypted (AES-256), without this password nobody can get in, not even
    you. Without passwords the field may stay empty.
  - On the other PC: "Import data …", choose the file, enter the password if needed, check
    the ticks, "Apply".
  - Nothing is overwritten, everything is merged: for identical words the higher count wins
    (so importing the same file twice does not count double). A macro with the same name is
    replaced. For a password with the same name and user, the one changed or used most
    recently wins.
  - The last folder used is remembered. A folder both PCs can see is handy (USB stick,
    cloud folder).
  - Size and position are not transferred, they differ from PC to PC anyway.

Support
-------
The keyboard is free and open source. If it helps you, I'd be happy about a donation:
https://ko-fi.com/MDM3D
The button is at the very top of the settings and in the tray menu. The link only opens when
you click it. The keyboard never asks for it.

Lock screen
-----------
To type the Windows PIN with this keyboard, it must appear on the lock screen. Windows only
allows this if all of the following apply:
  - the keyboard is signed with a code signing certificate,
  - it is installed with the installer to "C:\Program Files\NoHandsNeeded",
  - it is registered with Windows as an assistive technology (the installer does this),
  - it has the uiAccess permission (the NoHandsNeededAT.exe variant from build.cmd).
The portable NoHandsNeeded.exe cannot do this. Settings → Other → Lock screen shows what is
missing. When everything is in place, there is a switch "Show on lock screen".
On the lock screen only typing works: no vault, no macros, no learning, no word suggestions,
no settings. So nobody sees anything personal on the locked PC.
Until then: click the accessibility icon at the bottom right of the lock screen and use the
Windows on-screen keyboard. Or set up Windows Hello (face), then no PIN is needed.

Known limits
------------
The keyboard cannot type into programs running as administrator (Task Manager, some
installers). This is a Windows security rule. Workaround: right-click NoHandsNeeded.exe →
"Run as administrator", or use the signed, installed version (see "Lock screen").

The case key works on the last word typed with the keyboard. It cannot yet convert selected
text in other programs.

Word lists: 11 built-in languages with about 27,000 to 74,000 words each. Frequencies from
wordfreq (data under CC BY-SA 4.0), spelling checked with hunspell dictionaries.

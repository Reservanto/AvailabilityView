# Nahrávky Reservanto API

Rychlé testy běží nad nahranými odpověďmi produkčního API, bez sítě. Jeden soubor na scénář:
`<Controller>/<Action>/<scénář>.json`.

| Pole | Význam |
|---|---|
| `source` | odkud nahrávka je, `production` |
| `apiVersion` | verze OpenAPI, se kterou byl klient napsaný |
| `recordedAt` | čas nahrání, testy nad nahrávkami běží v tomto čase |
| `endpoint`, `scenario` | musí odpovídat cestě souboru, hlídá to kontrola |
| `request` | co klient poslal. Přehrávání podle něj vybírá nahrávku se stejnou službou, provozovnou a zaměřením |
| `response` | `status` a `body`, to vrátí falešné API |

## Co v nahrávkách není

Tokeny, e-maily, telefony a GUID jsou `REDACTED`, hlavičky se neukládají. Názvy, popisy a adresy zaměření, provozoven, služeb a zdrojů nahradí nahrávač neutrálním textem podle typu a Id, třeba `Zdroj 10173` (tabulka v `tests/Shared/NeutralNames.cs`). Id, časy a stavy zůstávají. Výjimkou je Id kalendáře u lekcí: je to číslo účtu a vede na jeho rezervační stránku, proto je v nahrávce příklad `1234` a adresa rezervace bez čísla termínu. Adresa provozovny nemá název, kraj ani polohu. Z rezervací zůstane jen začátek, konec, zdroj, stav a Id termínu. Hlídá to `RecordingRules`, tajné údaje navíc `SecretRules`. Před commitem obnovených nahrávek projdi diff.

`Merchant/GetInfo` se nenahrává vůbec, nese IČO, kontakty a adresy merchanta. Smoke test ho čte bez nahrávače.

## Obnova

Předpoklad: účet připojený v průvodci, token je v user-secrets aplikace. Testy API jen čtou.

```powershell
$env:RESERVANTO_RECORD = '1'
powershell -NoProfile -ExecutionPolicy Bypass -File eng/check.ps1 -Mode Smoke
Remove-Item Env:RESERVANTO_RECORD
```

Testy nad nahrávkami počítají s tím, co bylo v účtu v čase nahrání, například s plně obsazenou lekcí. Smoke test na to upozorní.

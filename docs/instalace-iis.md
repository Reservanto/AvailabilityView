# Instalace do IIS

Návod pro správce serveru, merchanta nebo Reservanto. Na konci běží jedna instance AvailabilityView pro jednoho merchanta na vlastní adrese s HTTPS.

> **Důležité:** Reservanto při připojení vrací token v adrese stránky `/Auth/Callback`. Výchozí log IIS adresu zapisuje i s parametry, token by v něm zůstal. Proto web v kroku 5 dostane log bez parametrů adresy. **Bez toho instanci nepřipojujte.**

Příkazy jsou pro Windows PowerShell spuštěný **jako správce**. Ověřeno na Windows 11 Pro s IIS 10. Příkazy pro Windows Server jsou uvedené zvlášť a na serveru zatím ověřené nejsou.

## Co budete potřebovat

- Windows Server 2019 nebo novější, nebo Windows 10/11 Pro, a práva správce.
- Adresu, na které instance poběží, třeba `https://volno.example.cz`, a certifikát HTTPS pro ni.
- Účet merchanta v Reservantu. ClientId a návratovou adresu `https://<adresa instance>/Auth/Callback` zaregistrujete až v průvodci na `/Setup`.
- Publikovanou aplikaci (krok 3).

## 1. Zapnutí IIS

Windows 10/11:

```powershell
Enable-WindowsOptionalFeature -Online -All -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-StaticContent, IIS-DefaultDocument, IIS-HttpErrors, IIS-HealthAndDiagnostics, IIS-HttpLogging, IIS-Security, IIS-RequestFiltering, IIS-ManagementConsole, IIS-ManagementScriptingTools
```

Windows Server:

```powershell
Install-WindowsFeature Web-Server, Web-Http-Logging, Web-Mgmt-Console, Web-Scripting-Tools
```

Ověření: služba běží a `http://localhost` ukáže úvodní stránku IIS.

```powershell
Get-Service W3SVC
```

## 2. ASP.NET Core Hosting Bundle

Hosting Bundle přidá do IIS modul, který spouští aplikace ASP.NET Core, a runtime .NET.

1. Na stránce https://dotnet.microsoft.com/download/dotnet/10.0 v části ASP.NET Core Runtime, řádek Windows, stáhněte **Hosting Bundle**.
2. Spusťte instalátor. IIS musí být zapnutý už před instalací. Když jste IIS zapnuli až potom, spusťte instalátor znovu a zvolte Repair.
3. Restartujte IIS, aby modul načetl:

```powershell
net stop was /y
net start w3svc
```

Ověření: modul je v IIS zaregistrovaný.

```powershell
Import-Module WebAdministration
Get-WebGlobalModule -Name AspNetCoreModuleV2
```

## 3. Publikace aplikace

Ze zdrojového kódu (stačí běžný účet, ne správce):

```powershell
dotnet publish src/AvailabilityView.Web/AvailabilityView.Web.csproj --configuration Release --output artifacts/publish
```

Ve složce `artifacts/publish` je aplikace včetně `web.config` pro IIS.

## 4. Složka webu

Zkopírujte publikaci do složky webu a dejte fondu aplikací právo ji číst. Fond `AvailabilityView` vznikne v kroku 5, právo jde nastavit až po něm, proto je příkaz `icacls` na konci kroku 5.

```powershell
robocopy artifacts\publish C:\inetpub\AvailabilityView /E
```

Složka webu nesmí být podsložkou jiného webu, třeba `C:\inetpub\wwwroot` výchozího webu IIS. Ten by soubory aplikace vydával jako běžné soubory, včetně `appsettings.Production.json` s tokenem.

`robocopy` končí kódem 1, když soubory zkopíroval. Kód 8 a vyšší je chyba.

## 5. Fond aplikací a web

Fond aplikací bez .NET Frameworku (aplikace běží v .NET 10 přes Hosting Bundle) a web s HTTPS.

```powershell
Import-Module WebAdministration
$site = 'AvailabilityView'
$path = 'C:\inetpub\AvailabilityView'

New-WebAppPool -Name $site
Set-ItemProperty "IIS:\AppPools\$site" -Name managedRuntimeVersion -Value ''
icacls $path /grant "IIS AppPool\${site}:(OI)(CI)RX"
```

Pak **jeden** ze dvou bloků: web na vlastní doméně (server), nebo zkouška na vývojovém počítači (níže).

Web na vlastní doméně s certifikátem z úložiště počítače (`Cert:\LocalMachine\My`). Místo `<doména instance>` napište doménu, na které instance poběží, třeba `volno.example.cz`, a místo `<otisk certifikátu>` otisk (Thumbprint) jejího certifikátu z výpisu `Get-ChildItem Cert:\LocalMachine\My`.

```powershell
New-Website -Name $site -PhysicalPath $path -ApplicationPool $site -HostHeader '<doména instance>' -Port 443 -Ssl -SslFlags 1
(Get-WebBinding -Name $site -Protocol https).AddSslCertificate('<otisk certifikátu>', 'My')
```

### Zkouška na vývojovém počítači

Testovací ClientId projektu má návratovou adresu `https://localhost:5001/Auth/Callback`, proto web poslouchá na portu 5001 s certifikátem „IIS Express Development Certificate“, který instaluje Visual Studio. Aplikace spuštěná přes `dotnet run` port 5001 také používá, před zkouškou ji zastavte.

```powershell
New-Website -Name $site -PhysicalPath $path -ApplicationPool $site -Port 5001 -Ssl
$thumbprint = (Get-ChildItem Cert:\LocalMachine\My | Where-Object FriendlyName -eq 'IIS Express Development Certificate').Thumbprint
(Get-WebBinding -Name $site -Protocol https).AddSslCertificate($thumbprint, 'My')
```

### Log IIS bez parametrů adresy

Povinné před připojením, pro obě varianty webu. Log webu přestane zapisovat parametry adresy (`cs-uri-query`) a hlavičku Referer, takže se do něj token z `/Auth/Callback` nedostane. Cesta, stav, čas a IP návštěvníka v logu zůstanou.

```powershell
Set-ItemProperty "IIS:\Sites\$site" -Name logFile.logExtFileFlags -Value 'Date,Time,ServerIP,Method,UriStem,ServerPort,UserName,ClientIP,UserAgent,HttpStatus,HttpSubStatus,Win32Status,BytesSent,BytesRecv,TimeTaken'
```

Ověření: v logu webu (`C:\inetpub\logs\LogFiles\W3SVC<číslo webu>`) je po dalším požadavku nový řádek `#Fields:` bez `cs-uri-query` a `cs(Referer)`. IIS zapisuje log se zpožděním asi minutu, hned ho zapíše příkaz `netsh http flush logbuffer`.

Když server používá centrální log pro všechny weby (`centralLogFileMode` v nastavení serveru), nastavení webu se neuplatní. Pole je pak potřeba odebrat v centrálním logu, jinak instanci na takovém serveru nepřipojujte.

## 6. Ověření

Instance bez tokenu nastartuje a čeká na připojení.

```powershell
(Invoke-WebRequest https://localhost:5001/Setup -UseBasicParsing).StatusCode
```

- `https://<adresa instance>/Setup` ukáže první krok průvodce připojením.
- `https://<adresa instance>/` napíše, že stránka ještě není připojená k Reservantu, a odkáže na průvodce.

Když web nenaběhne a místo stránky je chyba 500.xx, podívejte se nejdřív do Prohlížeče událostí (protokol Application, zdroj .NET Runtime nebo IIS AspNetCore Module V2). Když tam důvod není, zapněte dočasně v `web.config` výpis `stdoutLogEnabled="true"`, vytvořte ve složce webu podsložku `logs` s právem zápisu pro fond aplikací a podívejte se do souboru v ní. Pak výpis zase vypněte.

## 7. Připojení k Reservantu

Jen s logem bez parametrů adresy z kroku 5 a jen když `/Setup` odpovídá.

1. Otevřete `https://<adresa instance>/Setup` a projděte průvodce. Návratová adresa pro ClientId je `https://<adresa instance>/Auth/Callback`.
2. Po souhlasu v Reservantu ukáže průvodce token a část konfigurace s ním. Zkopírujte ji, token je vidět jen jednou.
3. Otevřete soubor konfigurace v Poznámkovém bloku spuštěném jako správce. Token nevkládejte do příkazu v PowerShellu, ten si příkazy ukládá do historie na disk.

```powershell
notepad C:\inetpub\AvailabilityView\appsettings.Production.json
```

4. Vložte zkopírovanou část a soubor uložte. Když soubor neexistuje, Poznámkový blok nabídne ho založit.
5. Soubor smí číst jen správci, systém a fond aplikací:

```powershell
icacls C:\inetpub\AvailabilityView\appsettings.Production.json /inheritance:r /grant:r "Administrators:F" "SYSTEM:F" "IIS AppPool\AvailabilityView:R"
```

6. V průvodci klikněte na Ověřit připojení. Aplikace soubor načte sama, restart není potřeba.

Ověření, že token neskončil v logu IIS: vyhledejte jeho začátek, třeba prvních 8 znaků. Výsledek má být prázdný.

```powershell
netsh http flush logbuffer
Select-String -Path C:\inetpub\logs\LogFiles\W3SVC*\*.log -Pattern '<začátek tokenu>'
```

## 8. Vložení do webu a vlastní CSS

Do `appsettings.Production.json` doplňte vedle sekce `Reservanto` sekci `AvailabilityView`. Ukázku ukáže i poslední krok průvodce.

```json
{
  "Reservanto": { "LongTimeToken": "<token>" },
  "AvailabilityView": {
    "AllowedFrameOrigins": [ "https://www.example.cz" ],
    "CustomCssUrl": "/custom/web.css"
  }
}
```

- `AllowedFrameOrigins`: weby, které smí stránku vložit přes iframe.
- `CustomCssUrl`: vlastní styl, podrobnosti v `docs/vzhled.md`. Soubor ve složce instance patří do `C:\inetpub\AvailabilityView\wwwroot\custom\`.
- Obě volby platí až po restartu. Neplatná hodnota nedovolí start, web pak hlásí chybu 500.30 a důvod je v Prohlížeči událostí.

```powershell
Restart-WebAppPool AvailabilityView
```

Kód pro web merchanta sestaví generátor na `https://<adresa instance>/Generator`.

## 9. Aktualizace

1. Publikujte novou verzi (krok 3).
2. Zastavte aplikaci souborem `app_offline.htm`. Dokud je ve složce webu, IIS místo aplikace vrací jeho obsah a soubory aplikace nejsou zamčené.
3. Zkopírujte novou verzi a soubor smažte.

```powershell
Set-Content C:\inetpub\AvailabilityView\app_offline.htm 'Probiha aktualizace, zkuste to prosim za chvili.'
robocopy artifacts\publish C:\inetpub\AvailabilityView /E
Remove-Item C:\inetpub\AvailabilityView\app_offline.htm
```

Nepoužívejte `robocopy /MIR` ani publikaci, která maže soubory navíc. Smazala by `appsettings.Production.json` s tokenem a vlastní CSS ve `wwwroot\custom`.

## Známé chování a omezení

- **Varování při startu.** V Prohlížeči událostí jsou po každém startu tři varování Data Protection o klíčích jen v paměti. Aplikace žádné cookies ani chráněná data nepoužívá, varování nevadí.
- **Chybové stránky IIS.** `web.config` z publikace nastavuje `httpErrors existingResponse="PassThrough"`, aby IIS hlášky a stránku „příliš mnoho požadavků“ nenahradil vlastní stránkou, ani když to server nastavuje pro všechny weby. Když má server oddíl `httpErrors` zamčený, web nenaběhne a hlásí chybu 500.19. Pak oddíl pro web odemkněte, nebo řádek `httpErrors` z `web.config` odeberte.
- **Log chyb HTTP.sys.** Když požadavek odmítne už Windows, třeba proto, že je fond aplikací zastavený, zapíše adresu i s parametry do `C:\Windows\System32\LogFiles\HTTPERR`. Proto připojujte jen tehdy, když `/Setup` odpovídá. Když připojení skončilo chybou místo stránky s tokenem, projděte i tento log.
- **Omezení počtu požadavků za proxy.** Instance omezuje počet požadavků podle IP návštěvníka, 120 za minutu. Za reverzní proxy vidí všechny návštěvníky pod jednou IP proxy a limit platí pro všechny dohromady.

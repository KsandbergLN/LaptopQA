param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'ui-translations.json')
)

$ErrorActionPreference = 'Stop'
$sourceFiles = @(
    (Join-Path $PSScriptRoot '..\V4\MainWindow.xaml'),
    (Join-Path $PSScriptRoot '..\Mac\MainWindow.axaml'),
    (Join-Path $PSScriptRoot '..\Mac\SettingsWindow.axaml')
)
$phrases = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in $sourceFiles) {
    $text = Get-Content -LiteralPath $file -Raw -Encoding UTF8
    foreach ($match in [regex]::Matches($text, '(?:Text|Content|ToolTip|ToolTip\.Tip|Title)="([^"]*[A-Za-z][^"]*)"')) {
        $value = [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value).Trim()
        if ($value -and $value -notmatch '^\{TemplateBinding ' -and $value -notmatch '^https?://') {
            [void]$phrases.Add($value)
        }
    }
}

@(
    'Settings','Language','Theme','Technician','not set','Save','Cancel','Close','Minimize',
    'Reset Settings','Restore','Warning','Error','Success','Waiting','Working','Passed','Failed',
    'Ignored','Unavailable','Loading','Ready','Search','Print','Zoom In','Zoom Out','Fit',
    'Yes','No','OK','Confirm','Serial Number','Computer Name','Date','Time','Status','Result',
    'No removable drive was detected.','Configuration saved.','Settings saved.',
    'Technician name is required.','Enter technician name','Continue','Refresh','Open','Delete',
    'Laptop QA Onboarding','Welcome to Laptop QA',
    'Enter the technician name to use on QA sheets and app records.',
    'Please enter the technician name.','QA Sheet Preview','Zoom out','Zoom in',
    'Print QA sheet','Print the QA sheet','Close QA sheet preview','Minimize QA sheet preview',
    'Print QA Sheet','Diagnostics Log','Hardware Details','Search log:','Clear',
    'Type here to filter the diagnostics log.','Clear diagnostics log search.',
    'Select any text and press Ctrl+C to copy.','Save hardware details to a text file.',
    'Close details.','Kris''s Keyboard Tester','TESTED','ACTIVE','Reset','LAST KEY',
    'ACTIVE KEYS','None','Technician','Service Tag','Asset','Warranty','Battery Health',
    'Computer','Manufacturer','Model','Asset Number','Generated','Hardware Specs','QA Results',
    'OVERALL','TASK','STATUS','DETAIL','Needs Attention','Incomplete','PASS','FAIL','IGNORED',
    'WARNING','IN PROGRESS','NOT RUN','Memory','GPU','Storage',
    'Wi-Fi connected or SSIDs visible','Ethernet adapter is Up',
    'Camera, audio restore, and Camera Roll cleanup','External display video verified',
    'Keyboard test result','Dell preboot diagnostics','USB ports verified','Battery health checked',
    'Hash and group tag checked','Laptop cleaned','Update Stockrooms','Trackpad working',
    'Physical condition suitable for use','Hash and group tag checked off.',
    'Hash and group tag not checked off.','Cleaned laptop checked off.',
    'Cleaned laptop not checked off.','User removal from laptop in Intune checked off.',
    'User removal from laptop in Intune not checked off.','Stockrooms updated.',
    'Stockrooms not updated.','Trackpad working checked off.','Trackpad working not checked off.',
    'Physical laptop condition confirmed suitable for use.',
    'Physical laptop condition not confirmed suitable for use.','Quality assurance summary',
    'Battery information unavailable in Windows cache','Not checked off.','Not available',
    ('Preparing the QA workspace' + [char]0x2026)
) | ForEach-Object { [void]$phrases.Add($_) }

@(
    'Camera Roll cleanup timeout seconds','ServiceNow request URL','ServiceNow type of request',
    'ServiceNow automation wait milliseconds','ServiceNow assignment group name',
    'ServiceNow assignment group sys ID',
    'Warranty uses Dell Command | Warranty. Leave the CLI path blank to use the packaged tool or the system-installed tool. Diagnostics folder is optional; leave it blank to auto-detect the Dell log on a small FAT32 removable drive.',
    'Reset all configuration defaults and remove the saved technician name. Saved QA sheets and logs are kept.',
    'Reset all defaults and remove the technician name. Saved QA output is kept.',
    'Close Config without saving changes.','Save the current macOS configuration and theme.',
    'Settings are shared with Laptop QA V4 so both apps use the same saved configuration.',
    'Diagnostics and local folders','Request URL','Type of request','Automation wait milliseconds',
    'Assignment group name','Assignment group sys ID','Camera cleanup timeout seconds',
    'Excellent','Good','Fair','Poor','Battery Health'
) | ForEach-Object { [void]$phrases.Add($_) }

$english = @($phrases | Sort-Object)
$languages = [ordered]@{
    'en-US'='en'; 'es-ES'='es'; 'fr-FR'='fr'; 'de-DE'='de'; 'pt-BR'='pt';
    'zh-CN'='zh-CN'; 'ja-JP'='ja'; 'hi-IN'='hi'; 'bn-IN'='bn'; 'ta-IN'='ta';
    'te-IN'='te'; 'mr-IN'='mr'; 'ar-SA'='ar'
}

Add-Type -AssemblyName System.Web.Extensions
$jsonReader = [System.Web.Script.Serialization.JavaScriptSerializer]::new()
$workingPath = "$OutputPath.building"
$catalogSource = if (Test-Path -LiteralPath $workingPath) { $workingPath } else { $OutputPath }
$existing = if (Test-Path -LiteralPath $catalogSource) {
    $jsonReader.DeserializeObject([IO.File]::ReadAllText($catalogSource, [Text.Encoding]::UTF8))
} else { $null }
function Invoke-TranslationRequest([string]$uri) {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try { return Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec 20 }
        catch {
            if ($attempt -eq 5) { throw }
            Start-Sleep -Milliseconds (400 * $attempt)
        }
    }
}
function Get-ExistingMap([string]$code) {
    $map = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    if ($null -eq $existing) { return $map }
    if (-not $existing.ContainsKey($code)) { return $map }
    foreach ($item in $existing[$code].GetEnumerator()) {
        if ($phrases.Contains($item.Key)) { $map[$item.Key] = [string]$item.Value }
    }
    return $map
}

$result = [ordered]@{}
$result['en-US'] = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
foreach ($phrase in $english) { $result['en-US'][$phrase] = $phrase }
[IO.File]::WriteAllText($workingPath, ($result | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))

foreach ($entry in $languages.GetEnumerator()) {
    if ($entry.Key -eq 'en-US') { continue }
    Write-Host "Building $($entry.Key)..."
    $map = Get-ExistingMap $entry.Key
    $result[$entry.Key] = $map
    $pending = @($english | Where-Object { -not $map.ContainsKey($_) })
    $batchSize = if ($entry.Key -eq 'ar-SA') { 1 } else { 12 }
    for ($offset = 0; $offset -lt $pending.Count; $offset += $batchSize) {
        $batch = @($pending[$offset..([Math]::Min($offset + $batchSize - 1, $pending.Count - 1))])
        $marker = 'LQASPLIT9173'
        $query = [string]::Join(" $marker ", $batch)
        $uri = 'https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=' +
            [uri]::EscapeDataString($entry.Value) + '&dt=t&q=' + [uri]::EscapeDataString($query)
        $response = Invoke-TranslationRequest $uri
        $translatedText = (($response[0] | ForEach-Object { [string]$_[0] }) -join '')
        $translated = @($translatedText -split '\s*LQASPLIT9173\s*')
        if ($translated.Count -ne $batch.Count) {
            $translated = @()
            foreach ($phrase in $batch) {
                $singleUri = 'https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=' +
                    [uri]::EscapeDataString($entry.Value) + '&dt=t&q=' + [uri]::EscapeDataString($phrase)
                $singleResponse = Invoke-TranslationRequest $singleUri
                $translated += (($singleResponse[0] | ForEach-Object { [string]$_[0] }) -join '').Trim()
                Start-Sleep -Milliseconds 50
            }
        }
        for ($index = 0; $index -lt $batch.Count; $index++) {
            $map[$batch[$index]] = $translated[$index]
        }
        $result[$entry.Key] = $map
        [IO.File]::WriteAllText($workingPath, ($result | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
        Start-Sleep -Milliseconds 80
    }
    $result[$entry.Key] = $map
    $checkpoint = $result | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($workingPath, $checkpoint, [Text.UTF8Encoding]::new($false))
}

$json = $result | ConvertTo-Json -Depth 5
if ($json -match '\u00C3\u0192|\u00C3\u201A|\u00E2\u20AC|\u00E0\u00A4|\u00E0\u00A6|\u00E0\u00AE|\uFFFD') {
    throw 'Translation generation stopped because the output contains character-encoding corruption.'
}
[IO.File]::WriteAllText($workingPath, $json, [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $workingPath -Destination $OutputPath -Force
Write-Host "Created $OutputPath with $($english.Count) phrases in $($languages.Count) languages."

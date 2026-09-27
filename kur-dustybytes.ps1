# teknesyum-ui template kur/kur.ps1
param([string]$AnahtarAdi = "", [switch]$Onar, [switch]$Prova, [switch]$Otomatik, [string]$Hedef = "")

$kaynak = Split-Path -Parent $MyInvocation.MyCommand.Path
$kok = $env:KUR_KOK

$hedefVarsayilan = Join-Path $env:LOCALAPPDATA "Programs\DustyBytes"
$adimAdlari = @(@("Yazma izni denetleniyor", 0), @("Sürüm indiriliyor", 6), @("İndirilen dosya doğrulanıyor", 70), @("Dosyalar yerleştiriliyor", 78), @("Kısayollar oluşturuluyor", 92))
$onarVar = $false

if ($kok) {
  $hedefVarsayilan = Join-Path $kok ("Programlar\" + "DustyBytes")
  $masaustu = Join-Path $kok "Masaustu"
  $menu = Join-Path $kok "BaslatMenusu"
  $gunluk = Join-Path $kok "Gunluk\kurulum.log"
} else {
  $masaustu = [Environment]::GetFolderPath("Desktop")
  $menu = [Environment]::GetFolderPath("Programs")
  $gunluk = Join-Path $env:LOCALAPPDATA "DustyBytes\kurulum.log"
}
if ($Hedef) { $hedefVarsayilan = $Hedef }

$S = [hashtable]::Synchronized(@{
  ad = "DustyBytes"
  altbaslik = "Disk temizleyici"
  depo = "Teknesyum/DustyBytes"
  varlik = "DustyBytes-win-x64.zip"
  exe = "DustyBytes.exe"
  anahtarAdi = $AnahtarAdi
  onar = [bool]$Onar
  kaynak = $kaynak
  hedef = $hedefVarsayilan
  masaustu = $masaustu
  menu = $menu
  gunluk = $gunluk
  sonuc = $env:KUR_SONUC
  adimlar = $adimAdlari
  yuzde = 0
  tavan = 0
  adim = "Kurulum yerini seç ve Kur düğmesine bas."
  log = [System.Collections.ArrayList]::Synchronized((New-Object System.Collections.ArrayList))
  durum = "hazir"
  hata = $null
  surum = ""
  kisayol = $null
  cevrimdisi = $false
  prova = ([bool]$Prova -or [bool]$env:KUR_PROVA)
  otomatik = ([bool]$Otomatik -or [bool]$env:KUR_OTOMATIK)
  baslat = $null
})

$is = {
  param($S)
  $ErrorActionPreference = "Continue"
  New-Item -ItemType Directory -Force (Split-Path $S.gunluk) | Out-Null
  function Yaz([string]$m) {
    $satir = (Get-Date -Format "HH:mm:ss") + "  " + $m
    [void]$S.log.Add($satir)
    Add-Content -Path $S.gunluk -Value $satir -Encoding UTF8
    if ($S.otomatik) { [Console]::Out.WriteLine($satir) }
  }
  function Adim([int]$y, [int]$t, [string]$m) { $S.yuzde = $y; $S.tavan = $t; $S.adim = $m; Yaz $m }
  function Durdur([string]$kok) {
    Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($kok, [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object {
      Yaz ("Kapatılıyor: " + $_.Name)
      Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 800
  }

  try {
    $hedef = $S.hedef
    if ($S.onar) { Yaz "Onarım: kurulum baştan yapılacak" }

    function Indir([string]$adres, [string]$yol, [int]$y0, [int]$y1) {
      $istek = [Net.HttpWebRequest]::Create($adres)
      $istek.UserAgent = $S.ad + "-kurulum"
      $istek.Timeout = 30000
      $istek.ReadWriteTimeout = 30000
      try { $yanit = $istek.GetResponse() } catch { throw ("İndirilemedi (" + (Split-Path -Leaf $yol) + "): " + $_.Exception.GetBaseException().Message) }
      $toplam = $yanit.ContentLength
      $akis = $yanit.GetResponseStream()
      $dosya = [IO.File]::Create($yol)
      $alinan = 0L
      try {
        $tampon = New-Object byte[] 262144
        while (($n = $akis.Read($tampon, 0, $tampon.Length)) -gt 0) {
          $dosya.Write($tampon, 0, $n)
          $alinan += $n
          if ($toplam -gt 0) { $S.yuzde = $y0 + [int](($y1 - $y0) * $alinan / $toplam) }
        }
      } finally { $dosya.Close(); $akis.Close(); $yanit.Close() }
      if ($toplam -gt 0 -and $alinan -ne $toplam) { throw "İndirme yarıda kaldı ($alinan / $toplam bayt). Bağlantıyı denetleyip Yeniden dene." }
    }

    $gecici = Join-Path $env:TEMP ("kur-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
    New-Item -ItemType Directory -Force $gecici | Out-Null
    $S.gecici = $gecici
    if ($S.prova) { $hedef = Join-Path $gecici ("prova\" + $S.ad); $S.hedef = $hedef; Yaz "Prova: geçici klasöre kurulur, kısayol yazılmaz" }
    Yaz ("Kaynak: github.com/" + $S.depo + " · " + $S.varlik)
    Yaz "Hedef : $hedef"

    Adim 0 6 "Yazma izni denetleniyor"
    $ust = Split-Path -Parent $hedef
    try {
      New-Item -ItemType Directory -Force $ust -ErrorAction Stop | Out-Null
      $dene = Join-Path $ust (".yazma-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
      [IO.File]::WriteAllText($dene, "")
      Remove-Item -LiteralPath $dene -Force
    } catch { throw "Bu klasöre yazılamıyor: $ust. Değiştir ile kullanıcı klasörünüzde bir yer seçin; yönetici yetkisi gerekmez." }
    Yaz "Yazma izni tamam: $ust"

    Adim 6 12 "Son sürüm soruluyor"
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $api = "https://api.github.com"
    if ($env:KUR_API) { $api = $env:KUR_API.TrimEnd("/"); Yaz "Sınama adresi: $api" }
    $basliklar = @{ "User-Agent" = $S.ad + "-kurulum"; Accept = "application/vnd.github+json" }
    try { $yayin = Invoke-RestMethod -Uri ($api + "/repos/" + $S.depo + "/releases/latest") -Headers $basliklar -TimeoutSec 30 -ErrorAction Stop }
    catch { throw ("GitHub'a ulaşılamadı, depo özel ya da yayımlanmış sürüm yok (" + $S.depo + "): " + $_.Exception.Message) }
    $zip = $yayin.assets | Where-Object { $_.name -eq $S.varlik } | Select-Object -First 1
    $ozet = $yayin.assets | Where-Object { $_.name -eq ($S.varlik + ".sha256") } | Select-Object -First 1
    if (-not $zip) { throw ("Sürüm " + $yayin.tag_name + " içinde " + $S.varlik + " yok.") }
    if (-not $ozet) { throw ("Sürüm " + $yayin.tag_name + " içinde " + $S.varlik + ".sha256 yok; doğrulanamayan dosya kurulmaz.") }
    $S.surum = [string]$yayin.tag_name
    Yaz ("Son sürüm: " + $S.surum + " · " + [math]::Round($zip.size / 1MB, 1) + " MB")

    Adim 12 70 "Sürüm indiriliyor"
    $zipYol = Join-Path $gecici $S.varlik
    Indir $zip.browser_download_url $zipYol 12 70
    Yaz "İndirildi: $($S.varlik)"

    Adim 70 78 "İndirilen dosya doğrulanıyor"
    $ozetYol = $zipYol + ".sha256"
    Indir $ozet.browser_download_url $ozetYol 70 72
    $beklenen = ([regex]::Match([IO.File]::ReadAllText($ozetYol), "\b[0-9a-fA-F]{64}\b")).Value.ToLowerInvariant()
    if (-not $beklenen) { throw ("Doğrulama dosyası okunamadı: " + $ozet.name) }
    $akisH = [IO.File]::OpenRead($zipYol)
    try { $gercek = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($akisH)) -replace "-", "").ToLowerInvariant() } finally { $akisH.Close() }
    if ($gercek -ne $beklenen) { throw "İndirilen dosya doğrulanamadı: SHA-256 tutmuyor. Dosya bozuk ya da değiştirilmiş; kurulum yapılmadı." }
    Yaz "SHA-256 doğrulandı: $gercek"

    Adim 78 92 "Dosyalar yerleştiriliyor"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $acik = Join-Path $gecici "acik"
    try { [IO.Compression.ZipFile]::ExtractToDirectory($zipYol, $acik) } catch { throw ("Paket açılamadı: " + $_.Exception.GetBaseException().Message) }
    $ic = @(Get-ChildItem -LiteralPath $acik -Force)
    if ($ic.Count -eq 1 -and $ic[0].PSIsContainer) { $acik = $ic[0].FullName }
    if (-not (Test-Path (Join-Path $acik $S.exe))) { throw ("Paketin içinde " + $S.exe + " yok; yanlış dosya indirilmiş olabilir.") }
    Yaz "Paket geçici klasöre açıldı"
    $S.yuzde = 84
    $eski = $hedef + ".eski"
    if (Test-Path $hedef) {
      Durdur $hedef
      Remove-Item -LiteralPath $eski -Recurse -Force -ErrorAction SilentlyContinue
      try { Rename-Item -LiteralPath $hedef -NewName (Split-Path -Leaf $eski) -ErrorAction Stop }
      catch { throw "Eski kurulum kullanımda, değiştirilemedi: $hedef. Programı kapatıp Yeniden dene." }
      Yaz "Eski sürüm kenara alındı"
    }
    try {
      if ([IO.Path]::GetPathRoot($acik) -eq [IO.Path]::GetPathRoot($hedef)) { Move-Item -LiteralPath $acik -Destination $hedef -ErrorAction Stop }
      else {
        robocopy $acik $hedef /E /MOVE /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy çıkış $LASTEXITCODE" }
      }
    } catch {
      Remove-Item -LiteralPath $hedef -Recurse -Force -ErrorAction SilentlyContinue
      if (Test-Path $eski) { Rename-Item -LiteralPath $eski -NewName (Split-Path -Leaf $hedef) -ErrorAction SilentlyContinue }
      throw ("Dosyalar yerleştirilemedi, eski sürüm geri kondu: " + $_)
    }
    Remove-Item -LiteralPath $eski -Recurse -Force -ErrorAction SilentlyContinue
    Yaz "Yerleştirildi: $hedef"
    @{ tarih = (Get-Date).ToString("s"); surum = $S.surum; depo = $S.depo; varlik = $S.varlik; sha256 = $gercek; hedef = $hedef } | ConvertTo-Json | Set-Content (Join-Path (Split-Path $S.gunluk) "kurulum.json") -Encoding UTF8
    Remove-Item -LiteralPath $gecici -Recurse -Force -ErrorAction SilentlyContinue

    Adim 92 98 "Kısayollar oluşturuluyor"
    $calistir = Join-Path $hedef $S.exe
    $S.baslat = $calistir
    if ($S.prova) { Yaz "Prova: kısayol yazılmadı" }
    else {
      $kabuk = New-Object -ComObject WScript.Shell
      foreach ($klasor in @($S.masaustu, $S.menu)) {
        New-Item -ItemType Directory -Force $klasor | Out-Null
        $kisayol = Join-Path $klasor ($S.ad + ".lnk")
        $lnk = $kabuk.CreateShortcut($kisayol)
        $lnk.TargetPath = $calistir
        $lnk.WorkingDirectory = $hedef
        $lnk.IconLocation = $calistir + ",0"
        $lnk.Save()
        Yaz "Kısayol: $kisayol"
      }
      $S.kisayol = Join-Path $S.masaustu ($S.ad + ".lnk")
    }

    if ($S.surum) { $son = "Kurulum tamamlandı · sürüm " + $S.surum } else { $son = "Kurulum tamamlandı" }
    Adim 100 100 $son
    $S.durum = "bitti"
  } catch {
    Yaz ("HATA: " + $_)
    $S.hata = [string]$_
    $S.adim = "Kurulum yarıda kaldı: " + $_
    $S.durum = "hata"
  }
  if ($S.gecici -and -not $S.prova) { Remove-Item -LiteralPath $S.gecici -Recurse -Force -ErrorAction SilentlyContinue }
  if ($S.sonuc) {
    @{ durum = $S.durum; hedef = $S.hedef; surum = $S.surum; kisayol = $S.kisayol; hata = $S.hata; prova = [bool]$S.prova } | ConvertTo-Json | Set-Content -LiteralPath $S.sonuc -Encoding UTF8
  }
}

if ($S.otomatik) {
  $S.durum = "calisiyor"
  & $is $S
  if ($S.durum -eq "bitti") { exit 0 } else { exit 1 }
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

function Renk([string]$h, [double]$a = 1) { [System.Drawing.Color]::FromArgb([int][math]::Round(255 * $a), [System.Drawing.ColorTranslator]::FromHtml($h)) }
function Aile([string]$zincir) {
  $kurulu = @((New-Object System.Drawing.Text.InstalledFontCollection).Families | ForEach-Object { $_.Name })
  $parca = $zincir.Split(",") | ForEach-Object { $_.Trim() }
  foreach ($a in $parca) { if ($kurulu -contains $a) { return $a } }
  $parca[-1]
}
function Yazi([double]$px, [string]$stil, [string]$aile) { New-Object System.Drawing.Font($aile, [single]$px, [System.Drawing.FontStyle]$stil, [System.Drawing.GraphicsUnit]::Pixel) }

$R = @{
  zemin = Renk "#000000"; metin = Renk "#ffffff"; etiket = Renk "#6fb7ff"
  renk1 = Renk "#6fb7ff"; renk2 = Renk "#cba7d2"; vurgu = Renk "#fa8cff"
  basari = Renk "#66f09a"; tehlike = Renk "#fa8cff"; edilgen = Renk "#8a8f9a"
  ustu1 = Renk "#000000"; kenar = Renk "#6fb7ff" 0.55; iz = Renk "#6fb7ff" 0.2
  suren = Renk "#6fb7ff" 0.2
}
$pencereKenari = ""
$O = @{
  fs1 = 14; fs2 = 16; fs4 = 24; satir = 24; baslikSatir = 1.25
  b2 = 8; b3 = 12; b4 = 16; b5 = 24
  ikon = 56; isaret = 24; dugmeY = 28; dugmePx = 10
  cizgi = 1; genislik = 560
}
$sans = Aile "Atkinson Hyperlegible Next,Segoe UI"
$mono = Aile "Cascadia Mono,Consolas"
$YZ = @{
  baslik = Yazi $O.fs4 "Bold" $sans; govde = Yazi $O.fs2 "Regular" $sans; guclu = Yazi $O.fs2 "Bold" $sans
  log = Yazi $O.fs2 "Regular" $mono; isaret = Yazi $O.fs2 "Bold" $mono; dugme = Yazi $O.fs2 "Bold" $sans
}
$B = @{}
foreach ($k in $R.Keys) { $B[$k] = New-Object System.Drawing.SolidBrush $R[$k] }
$G = @{ goster = 0.0; surukle = $null; ikon = $null; dugmeler = @(); ps = $null; rs = $null }
$LOG_SATIRI = 5

$y = $O.b5
$L = @{ baslik = $y }
$y += $O.ikon + $O.b4; $L.adimlar = $y
$y += $S.adimlar.Count * ($O.isaret + $O.b2) - $O.b2 + $O.b4; $L.cubuk = $y
$y += [int]($O.fs2 * $O.baslikSatir) + $O.b3; $L.log = $y
$y += $LOG_SATIRI * $O.satir + $O.b4; $L.yer = $y
$y += $O.dugmeY + $O.b5; $L.dugme = $y
$y += $O.dugmeY + $O.b5; $L.yukseklik = $y

$f = New-Object System.Windows.Forms.Form
$f.Text = $S.ad + " Kurulum"
$f.FormBorderStyle = "None"
$f.StartPosition = "CenterScreen"
$f.ClientSize = New-Object System.Drawing.Size($O.genislik, $L.yukseklik)
$f.BackColor = $R.zemin
$f.ForeColor = $R.metin
$f.KeyPreview = $true
$f.GetType().GetProperty("DoubleBuffered", [Reflection.BindingFlags]"Instance,NonPublic").SetValue($f, $true, $null)
$simgeYol = Join-Path $kaynak "src\DustyBytes.App\Assets\DustyBytes.ico"
if (Test-Path $simgeYol) {
  $f.Icon = New-Object System.Drawing.Icon($simgeYol)
  $G.ikon = (New-Object System.Drawing.Icon($simgeYol, 256, 256)).ToBitmap()
}

function SuAnkiAdim {
  $i = 0
  for ($k = 0; $k -lt $S.adimlar.Count; $k++) { if ($S.yuzde -ge $S.adimlar[$k][1]) { $i = $k } }
  $i
}

function Dugme([string]$eylem, [string]$metin, [bool]$birincil, [bool]$etkin) { @{ eylem = $eylem; metin = $metin; birincil = $birincil; etkin = $etkin; alan = $null } }

function Dugmeler {
  $w = $f.ClientSize.Width
  $calisiyor = $S.durum -eq "calisiyor"
  $alt = @(switch ($S.durum) {
    "hazir" { @((Dugme "kapat" "Kapat" $false $true), (Dugme "kur" "Kur" $true $true)) }
    "calisiyor" { @((Dugme "kapat" "Kapat" $false $false), (Dugme "yok" "Kuruluyor" $true $false)) }
    "bitti" {
      $d = @()
      if ($onarVar) { $d += Dugme "onar" "Onar" $false $true }
      $d += Dugme "kapat" "Kapat" $false $true
      if (-not $S.prova -and $S.baslat) { $d += Dugme "ac" "Programı aç" $true $true }
      $d
    }
    default { @((Dugme "gunluk" "Günlüğü aç" $false $true), (Dugme "kapat" "Kapat" $false $true), (Dugme "yeniden" "Yeniden dene" $true $true)) }
  })
  $olc = [System.Windows.Forms.TextRenderer]
  $x = $w - $O.b5
  for ($i = $alt.Count - 1; $i -ge 0; $i--) {
    $gen = $olc::MeasureText($alt[$i].metin, $YZ.dugme).Width + 2 * $O.dugmePx
    $x -= $gen
    $alt[$i].alan = New-Object System.Drawing.Rectangle($x, $L.dugme, $gen, $O.dugmeY)
    $x -= $O.b3
  }
  $degistir = Dugme "degistir" "Değiştir" $false ($S.durum -eq "hazir" -or $S.durum -eq "hata")
  $gen = $olc::MeasureText($degistir.metin, $YZ.dugme).Width + 2 * $O.dugmePx
  $degistir.alan = New-Object System.Drawing.Rectangle(($w - $O.b5 - $gen), $L.yer, $gen, $O.dugmeY)
  @($degistir) + $alt
}

$f.Add_Paint({
  $cz = $_.Graphics
  $cz.SmoothingMode = "AntiAlias"
  $cz.TextRenderingHint = "ClearTypeGridFit"
  $w = $f.ClientSize.Width
  $h = $f.ClientSize.Height
  $P = $O.b5
  $bicim = New-Object System.Drawing.StringFormat
  $bicim.Trimming = "EllipsisCharacter"
  $bicim.FormatFlags = "NoWrap"
  $bicim.LineAlignment = "Center"
  $sarBicim = New-Object System.Drawing.StringFormat
  $sarBicim.Trimming = "EllipsisWord"
  $orta = New-Object System.Drawing.StringFormat
  $orta.Alignment = "Center"
  $orta.LineAlignment = "Center"
  if ($pencereKenari) { $cz.DrawRectangle((New-Object System.Drawing.Pen((Renk $pencereKenari 1), $O.cizgi)), 0, 0, $w - 1, $h - 1) }

  $metinX = $P
  if ($G.ikon) { $cz.DrawImage($G.ikon, $P, $L.baslik, $O.ikon, $O.ikon); $metinX = $P + $O.ikon + $O.b3 }
  $baslikY = [int]($O.fs4 * $O.baslikSatir)
  $cz.DrawString($S.ad, $YZ.baslik, $B.renk1, (New-Object System.Drawing.RectangleF($metinX, $L.baslik, ($w - $metinX - $P), $baslikY)), $bicim)
  $gen = [System.Windows.Forms.TextRenderer]::MeasureText($S.ad, $YZ.baslik).Width
  $cz.DrawString("Kurulum", $YZ.baslik, $B.vurgu, (New-Object System.Drawing.RectangleF(($metinX + $gen), $L.baslik, ($w - $metinX - $gen - $P), $baslikY)), $bicim)
  if ($S.durum -eq "hata") { $alt = "Günlük  ·  " + $S.gunluk; $altFirca = $B.tehlike }
  elseif ($S.surum) { $alt = "Sürüm " + $S.surum; $altFirca = $B.metin }
  elseif ($S.altbaslik) { $alt = $S.altbaslik; $altFirca = $B.metin }
  else { $alt = "github.com/" + $S.depo; $altFirca = $B.metin }
  $cz.DrawString($alt, $YZ.govde, $altFirca, (New-Object System.Drawing.RectangleF($metinX, ($L.baslik + $baslikY + $O.b2), ($w - $metinX - $P), [int]($O.fs2 * $O.baslikSatir))), $bicim)

  $suan = SuAnkiAdim
  $ay = $L.adimlar
  for ($i = 0; $i -lt $S.adimlar.Count; $i++) {
    if ($S.durum -eq "bitti" -or ($S.durum -ne "hazir" -and $i -lt $suan)) { $tur = "bitti" }
    elseif ($S.durum -eq "hata" -and $i -eq $suan) { $tur = "hata" }
    elseif ($S.durum -eq "calisiyor" -and $i -eq $suan) { $tur = "suren" }
    else { $tur = "" }
    $kutu = New-Object System.Drawing.Rectangle($P, $ay, $O.isaret, $O.isaret)
    switch ($tur) {
      "bitti" { $kalem = $R.basari; $yazi = $B.basari; $isaret = [string][char]0x2713 }
      "hata" { $kalem = $R.tehlike; $yazi = $B.tehlike; $isaret = "!" }
      "suren" { $kalem = $R.renk1; $yazi = $B.renk1; $isaret = [string]($i + 1); $cz.FillRectangle((New-Object System.Drawing.SolidBrush $R.suren), $kutu) }
      default { $kalem = $R.kenar; $yazi = $B.metin; $isaret = [string]($i + 1) }
    }
    $cz.DrawRectangle((New-Object System.Drawing.Pen($kalem, $O.cizgi)), $kutu.X, $kutu.Y, $kutu.Width - 1, $kutu.Height - 1)
    $cz.DrawString($isaret, $YZ.isaret, $yazi, (New-Object System.Drawing.RectangleF($kutu.X, $kutu.Y, $kutu.Width, $kutu.Height)), $orta)
    if ($tur -eq "suren") { $adYazi = $YZ.guclu } else { $adYazi = $YZ.govde }
    $cz.DrawString($S.adimlar[$i][0], $adYazi, $yazi, (New-Object System.Drawing.RectangleF(($P + $O.isaret + $O.b3), $ay, ($w - 2 * $P - $O.isaret - $O.b3), $O.isaret)), $bicim)
    $ay += $O.isaret + $O.b2
  }

  $renk = switch ($S.durum) { "bitti" { $R.basari } "hata" { $R.tehlike } default { $R.renk1 } }
  $yuzdeMetin = [string][math]::Floor($G.goster) + "%"
  $yg = [System.Windows.Forms.TextRenderer]::MeasureText("100%", $YZ.isaret).Width
  $satirY = [int]($O.fs2 * $O.baslikSatir)
  $sag = New-Object System.Drawing.StringFormat
  $sag.Alignment = "Far"
  $sag.LineAlignment = "Center"
  $cz.DrawString($yuzdeMetin, $YZ.isaret, (New-Object System.Drawing.SolidBrush $renk), (New-Object System.Drawing.RectangleF(($w - $P - $yg), $L.cubuk, $yg, $satirY)), $sag)
  $bx = $P; $bw = $w - 2 * $P - $yg - $O.b3; $bh = $O.b2; $by = $L.cubuk + [int](($satirY - $bh) / 2)
  $cz.FillRectangle($B.iz, $bx, $by, $bw, $bh)
  $dolu = [int]($bw * [math]::Min(100, $G.goster) / 100)
  if ($dolu -gt 1) {
    $dik = New-Object System.Drawing.Rectangle($bx, $by, $dolu, $bh)
    if ($S.durum -eq "calisiyor") { $fr = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Rectangle($bx, $by, $bw, $bh)), $R.renk1, $R.renk2, 0.0) }
    else { $fr = New-Object System.Drawing.SolidBrush $renk }
    $cz.FillRectangle($fr, $dik)
  }

  $satirlar = @($S.log.ToArray())
  if (-not $satirlar.Count) { $satirlar = @($S.adim) }
  $n = $satirlar.Count
  $hataVar = $S.durum -eq "hata"
  $sigan = $LOG_SATIRI
  if ($hataVar) { $sigan = $LOG_SATIRI - 2 }
  $bas = [math]::Max(0, $n - $sigan)
  $ly = $L.log
  for ($i = $bas; $i -lt $n; $i++) {
    $sira = $i - $bas
    $gorunen = $n - $bas
    $yuk = $O.satir
    $bc = $bicim
    if ($i -eq $n - 1) {
      if ($hataVar) { $lr = $R.tehlike; $yuk = $O.satir * 3; $bc = $sarBicim } else { $lr = $R.metin }
    } else { $lr = $R.etiket }
    if (-not $hataVar -and $gorunen -eq $LOG_SATIRI -and $sira -eq 0) { $lr = [System.Drawing.Color]::FromArgb([int](255 * 0.3), $lr) }
    elseif (-not $hataVar -and $gorunen -eq $LOG_SATIRI -and $sira -eq 1) { $lr = [System.Drawing.Color]::FromArgb([int](255 * 0.6), $lr) }
    $cz.DrawString($satirlar[$i], $YZ.log, (New-Object System.Drawing.SolidBrush $lr), (New-Object System.Drawing.RectangleF($P, $ly, ($w - 2 * $P), $yuk)), $bc)
    $ly += $yuk
  }

  $G.dugmeler = Dugmeler
  $degistir = $G.dugmeler[0]
  $etiketMetin = "Kurulum yeri"
  $eg = [System.Windows.Forms.TextRenderer]::MeasureText($etiketMetin, $YZ.guclu).Width
  $cz.DrawString($etiketMetin, $YZ.guclu, $B.etiket, (New-Object System.Drawing.RectangleF($P, $L.yer, $eg, $O.dugmeY)), $bicim)
  $yolX = $P + $eg + $O.b3
  $yolBicim = New-Object System.Drawing.StringFormat
  $yolBicim.Trimming = "EllipsisPath"
  $yolBicim.FormatFlags = "NoWrap"
  $yolBicim.LineAlignment = "Center"
  $cz.DrawString($S.hedef, $YZ.log, $B.metin, (New-Object System.Drawing.RectangleF($yolX, $L.yer, ($degistir.alan.X - $yolX - $O.b3), $O.dugmeY)), $yolBicim)

  foreach ($d in $G.dugmeler) {
    $a = $d.alan
    if ($d.birincil -and $d.etkin) {
      $cz.FillRectangle($B.renk1, $a)
      $df = $B.ustu1
    } else {
      if ($d.etkin) { $kr = $R.renk1; $df = $B.metin } else { $kr = $R.edilgen; $df = $B.edilgen }
      $cz.DrawRectangle((New-Object System.Drawing.Pen($kr, $O.cizgi)), $a.X, $a.Y, $a.Width - 1, $a.Height - 1)
    }
    $cz.DrawString($d.metin, $YZ.dugme, $df, (New-Object System.Drawing.RectangleF($a.X, $a.Y, $a.Width, $a.Height)), $orta)
  }
})

function Baslat {
  $S.log.Clear()
  $S.yuzde = 0
  $S.tavan = 2
  $S.hata = $null
  $S.durum = "calisiyor"
  $G.goster = 0.0
  if ($G.ps) { $G.ps.Dispose() }
  $G.ps = [powershell]::Create()
  $G.ps.Runspace = $G.rs
  [void]$G.ps.AddScript($is).AddArgument($S)
  [void]$G.ps.BeginInvoke()
}

function YerSec {
  $sec = New-Object System.Windows.Forms.FolderBrowserDialog
  $sec.Description = "Kurulum yeri: " + $S.ad + " bu klasörün içine kurulur."
  $sec.SelectedPath = Split-Path -Parent $S.hedef
  if ($sec.ShowDialog($f) -eq "OK") {
    if ((Split-Path -Leaf $sec.SelectedPath) -eq $S.ad) { $S.hedef = $sec.SelectedPath } else { $S.hedef = Join-Path $sec.SelectedPath $S.ad }
    if ($S.durum -eq "hata") { $S.durum = "hazir"; $S.log.Clear(); $S.yuzde = 0; $G.goster = 0.0; $S.adim = "Kurulum yeri değişti. Kur düğmesine bas." }
  }
}

function Eylem([string]$e) {
  switch ($e) {
    "kur" { Baslat }
    "yeniden" { Baslat }
    "onar" { $S.onar = $true; Baslat }
    "degistir" { YerSec }
    "kapat" { $f.Close() }
    "ac" { Start-Process $S.baslat; $f.Close() }
    "gunluk" { Start-Process notepad.exe $S.gunluk }
  }
}

function Vurulan($nokta) {
  foreach ($d in $G.dugmeler) { if ($d.alan -and $d.alan.Contains($nokta)) { return $d } }
  $null
}

$f.Add_MouseDown({
  if ($_.Button -ne "Left") { return }
  if (-not (Vurulan $_.Location)) { $G.surukle = $_.Location }
})
$f.Add_MouseMove({
  if ($G.surukle) { $f.Location = New-Object System.Drawing.Point(($f.Location.X + $_.X - $G.surukle.X), ($f.Location.Y + $_.Y - $G.surukle.Y)); return }
  $d = Vurulan $_.Location
  if ($d -and $d.etkin) { $f.Cursor = "Hand" } else { $f.Cursor = "Default" }
})
$f.Add_MouseUp({
  if ($G.surukle) { $G.surukle = $null; return }
  $d = Vurulan $_.Location
  if ($d -and $d.etkin) { Eylem $d.eylem }
})
$f.Add_KeyDown({
  if ($_.KeyCode -eq "Escape" -and $S.durum -ne "calisiyor") { $f.Close() }
  if ($_.KeyCode -eq "Return") {
    $d = $G.dugmeler | Where-Object { $_.birincil -and $_.etkin } | Select-Object -First 1
    if ($d) { Eylem $d.eylem }
  }
})
$f.Add_FormClosing({ if ($S.durum -eq "calisiyor") { $_.Cancel = $true } })

$zaman = New-Object System.Windows.Forms.Timer
$zaman.Interval = 16
$zaman.Add_Tick({
  $hy = [double]$S.yuzde
  if ($G.goster -lt $hy) { $G.goster = [math]::Min($hy, $G.goster + [math]::Max(0.2, ($hy - $G.goster) * 0.08)) }
  elseif ($S.durum -eq "calisiyor" -and $G.goster -lt ($S.tavan - 0.5)) { $G.goster += ($S.tavan - $G.goster) * 0.006 }
  $f.Invalidate()
})

$G.rs = [runspacefactory]::CreateRunspace()
$G.rs.ApartmentState = "STA"
$G.rs.Open()
$f.Add_Shown({ $zaman.Start(); if ($env:KUR_BASLA) { Baslat } })
[System.Windows.Forms.Application]::Run($f)
$zaman.Stop()
$G.rs.Close()

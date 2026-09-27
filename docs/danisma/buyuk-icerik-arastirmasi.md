# DustyBytes — Büyük ve Anlamlı İçerik Kataloğu Araştırması

Tarih: 2026-09-27. Web araştırmasıyla hazırlandı, her iddianın yanında kaynak linki var.
Doğrulanamayan bilgiler **(doğrulanmadı)** olarak işaretlendi. Yol ve id'ler İngilizce/orijinal
haliyle bırakıldı; açıklama cümleleri Türkçe.

Amaç: DustyBytes'ın "600MB önbellek" yerine "LM Studio: 40 GB dil modeli" gibi büyük ve anlamlı
öğeleri düz dilde adlandırıp, silme yerine 7 günlük karantinaya taşıması için gereken uygulama-
farkında kural tabanı. Her grupta önce özet tablo, sonra id başına ayrıntı (özel konum tespiti,
birim/isimlendirme, silinirse ne olur, kapalıyken güvenlik notu, kaynaklar).

---

## Grup 1 — Yerel Yapay Zeka / Dil Modeli Araçları

| id | Uygulama | İçerik türü | Varsayılan yol | Tipik boyut | Kapalıyken güvenli mi |
|---|---|---|---|---|---|
| `lm-studio` | LM Studio | dil modeli (GGUF/MLX) | `%USERPROFILE%\.lmstudio\models\<yayıncı>\<model>\` ve eski `%USERPROFILE%\.cache\lm-studio\models\` | 7B ~4-5GB, 13B ~7-8GB, 70B ~38-42GB | Evet |
| `ollama` | Ollama | model blob+manifest | `%USERPROFILE%\.ollama\models\` (manifests + blobs) | 7B ~4-5GB, 70B ~39-42GB | Evet, ama blob paylaşımına dikkat |
| `huggingface-hub-cache` | Hugging Face Hub cache | model/dataset önbelleği | `%USERPROFILE%\.cache\huggingface\hub\` | 400MB-15GB+ repo başına | Evet |
| `gpt4all` | GPT4All (Nomic AI) | GGUF dil modeli | `%LOCALAPPDATA%\nomic.ai\GPT4All\` | 7B ~4GB, 13B ~7-8GB | Evet |
| `jan-ai` | Jan (Menlo Research) | GGUF/MLX dil modeli | `%APPDATA%\Jan\data\llamacpp\models\<org>\<repo>\` | 7B ~4-5GB, 13B ~7-8GB | Evet |
| `text-generation-webui` | text-generation-webui (oobabooga) | dil modeli dosyası | `<kurulum-klasörü>\text-generation-webui\models\` | 7B 4-14GB, 70B 35-140GB | Evet |
| `automatic1111-webui` | AUTOMATIC1111 (stable-diffusion-webui) | görsel üretim modeli | `<kurulum-klasörü>\stable-diffusion-webui\models\Stable-diffusion\` | SD1.5 ~2-4GB, SDXL ~6.5-7GB, LoRA 10-400MB | Evet |
| `comfyui` | ComfyUI | görsel üretim modeli | `ComfyUI_windows_portable\ComfyUI\models\{checkpoints,loras,vae,...}\` | SD1.5 2-4GB, SDXL 6.5-7GB | Evet |
| `sd-forge` | Stable Diffusion Forge (lllyasviel) | görsel üretim modeli | A1111 ile aynı yerleşim, kendi kurulum klasöründe | A1111 ile aynı | Evet |
| `invokeai` | InvokeAI | görsel üretim modeli | `%USERPROFILE%\invokeai\models\` | SD1.5 2-4GB, SDXL 6.5-7GB | Evet, ama iç veritabanı bayatlayabilir |
| `pinokio` | Pinokio | kurulu yapay zeka uygulaması | kullanıcı seçimi ana klasör `\api\<uygulama-adı>\` (yaygın: `C:\pinokio\api\`) | uygulama başına 5-50GB+, bazıları 100GB'a kadar | Evet (paylaşılan `bin\` hariç) |

### `lm-studio`
- Özel konum: Ayarlar → My Models üzerinden değiştirilebiliyor; tam config dosyası/anahtar adı
  resmi kaynaklarda bulunamadı **(doğrulanmadı)**. Pratikte hem `.lmstudio` hem eski
  `.cache\lm-studio` klasörü taranmalı — GitHub issue #996 eski `.cache` klasörünün körlemesine
  silinmemesi gerektiğini vurguluyor.
- Birim/isim: her `yayıncı/model` alt klasörü bir birim; isim klasör yolundan türetilir.
- Silinirse: "Model silinir; LM Studio'dan tekrar indirebilirsin."
- Kaynak: [LM Studio Docs](https://lmstudio.ai/docs/app/basics/download-model), [filepathgeek.com](https://filepathgeek.com/posts/lm-studio-models-location/), [GitHub issue #996](https://github.com/lmstudio-ai/lmstudio-bug-tracker/issues/996)

### `ollama`
- Özel konum: `OLLAMA_MODELS` ortam değişkeni (config dosyası değil, Windows ortam değişkeni).
- Birim/isim: manifest `models\manifests\<host>\<namespace>\<model>\<tag>` yolunda; isim
  `<namespace>/<model>:<tag>`. Blob'lar (`models\blobs\sha256-<hash>`) manifestler arası
  paylaşılabildiği için bir modeli silerken yalnızca başka hiçbir manifestin referans vermediği
  blob'lar silinmeli (tıpkı `ollama rm` mantığı) — aksi halde diğer modeller bozulabilir.
- Silinirse: "Model silinir; Ollama'dan tekrar indirebilirsin (`ollama pull`)."
- Kaynak: [Ollama Windows Docs](https://docs.ollama.com/windows), [DeepWiki — storage/blobs](https://deepwiki.com/ollama/ollama/2.4-storage-and-blob-transfer)

### `huggingface-hub-cache`
- Özel konum: `HF_HOME` (tüm `.cache/huggingface` kökünü taşır) ve `HF_HUB_CACHE` (sadece `hub`
  alt klasörünü taşır) ortam değişkenleri.
- Birim/isim: her repo `models--<org>--<isim>` klasörü; insan-okunur isim `--`'leri `/`'ye
  çevirerek elde edilir (HF'nin kendi `hf cache ls` aracı en güvenilir kaynak). `blobs/` gerçek
  içerik, `snapshots/<commit>/` sembolik bağlantılar — Windows'ta Geliştirici Modu kapalıysa
  bunlar gerçek kopyalar olabilir, bu yüzden bütün `models--org--name` klasörünü tek birim
  olarak silmek daha güvenli.
- Silinirse: "İndirilen model/veri kümesi kopyası silinir; ihtiyaç duyan araç Hugging Face'ten
  tekrar indirir."
- Kaynak: [HF Hub — manage-cache](https://huggingface.co/docs/huggingface_hub/en/guides/manage-cache), [HF Hub — local-cache](https://huggingface.co/docs/hub/local-cache)

### `gpt4all`
- Özel konum: `%LOCALAPPDATA%\nomic.ai\GPT4All.ini` dosyasında `modelPath` anahtarı — bu bilgi
  yalnızca bir GitHub tartışma konusuyla doğrulandı, resmi dokümanla teyit edilemedi
  **(doğrulanmadı: tam ini yolu)**.
- Birim/isim: `models` klasöründeki her `.gguf` dosyası bir birim; isim dosya adı.
- Silinirse: "Model silinir; GPT4All'dan tekrar indirebilirsin."
- Kaynak: [GitHub issue #101](https://github.com/nomic-ai/gpt4all-chat/issues/101), [GPT4All Desktop Settings](https://docs.gpt4all.io/gpt4all_desktop/settings.html)

### `jan-ai`
- Özel konum: `settings.json` içindeki `data_folder` anahtarı (veri klasörünü taşımak için).
- Birim/isim: `llamacpp/models` altında `org/repo` klasörü veya `mlx/models` altında `model_id`
  klasörü; isim klasör yolu veya içindeki `model.yml` manifestinden.
- Silinirse: "Model silinir; Jan'dan tekrar indirebilirsin."
- Kaynak: [Jan Docs — data-folder](https://www.jan.ai/docs/desktop/data-folder)

### `text-generation-webui`
- Özel konum: sabit bir config anahtarı doğrulanamadı; `--model-dir` komut satırı bayrağı
  kullanılıyor **(doğrulanmadı: config.yaml anahtarı var mı)**. Klasör, taşınabilir kurulumun
  neresine kopyalandıysa orada — AppData değil.
- Birim/isim: `models\` altındaki her alt klasör/dosya bir birim; isim genelde zaten
  okunabilir (örn. `TheBloke_Llama-2-13B-GGUF`).
- Silinirse: "Model silinir; araç tekrar indirene kadar kullanılamaz."
- Kaynak: [GitHub discussion #232](https://github.com/oobabooga/text-generation-webui/discussions/232)

### `automatic1111-webui` / `sd-forge`
- Özel konum: JSON config yok; `webui-user.bat` içindeki `COMMANDLINE_ARGS` satırında
  `--ckpt-dir`, `--lora-dir`, `--vae-dir`, `--embeddings-dir`, `--hypernetwork-dir` bayrakları
  aranır. Forge ayrıca `--forge-ref-a1111-home <yol>` ile mevcut bir A1111 kurulumunun model
  klasörlerini paylaşabilir.
- Birim/isim: `Stable-diffusion\`, `Lora\`, `VAE\`, `embeddings\` altındaki her dosya bir birim;
  isim dosya adı.
- Silinirse: "Görsel üretim modeli/eklentisi silinir; tekrar indirilmeden kullanılamaz."
- Kaynak: [GitHub discussion #5053](https://github.com/AUTOMATIC1111/stable-diffusion-webui/discussions/5053), [Forge discussion #206](https://github.com/lllyasviel/stable-diffusion-webui-forge/discussions/206)

### `comfyui`
- Özel konum: taşınabilir sürümde `extra_model_paths.yaml` (kök dizinde), Desktop sürümde
  `%APPDATA%\ComfyUI\extra_models_config.yaml` — `base_path` anahtarı paylaşılan model kökünü
  belirler, tip başına alt anahtarlar (`checkpoints:`, `loras:`, `vae:` vb.) vardır.
- Birim/isim: `models\<tip>\` altındaki her dosya bir birim; isim dosya adı.
- Silinirse: "Görsel üretim modeli/kontrol dosyası silinir; tekrar indirilmeden kullanılamaz."
- Kaynak: [ComfyUI Wiki — files](https://comfyui-wiki.com/en/interface/files), [ComfyUI Docs — models](https://docs.comfy.org/development/core-concepts/models)

### `invokeai`
- Özel konum: kök klasör sırası `--root` bayrağı → `INVOKEAI_ROOT` ortam değişkeni → aktif
  virtualenv → varsayılan `~/invokeai`; kök içindeki `invokeai.yaml` dosyasında `models_dir`
  anahtarı sadece model klasörünü taşıyabilir.
- Birim/isim: `models\` altında taban/tipe göre organize edilmiş (`models\sd-1\main\` vb.);
  InvokeAI dahili bir SQLite veritabanında model kaydı tutuyor — dosya elle silinirse veritabanı
  geçici olarak bayatlar, tarama ile düzelir (veri kaybı yok, sadece yeniden tarama gerekir).
- Silinirse: "Görsel üretim modeli silinir; tekrar indirilmeden/içe aktarılmadan kullanılamaz."
- Kaynak: [InvokeAI — invokeai-yaml](https://invoke.ai/configuration/invokeai-yaml/)

### `pinokio`
- Özel konum: `%APPDATA%\Pinokio\config.json` içinde ev/bin yolları tutuluyor, tam anahtar adı
  doğrulanamadı **(doğrulanmadı)**; ayarlar ekranı (dişli ikonu) en güvenilir kaynak.
- Birim/isim: `<ev-klasörü>\api\<uygulama-adı>\` altındaki her klasör bir birim (tam uygulama,
  kendi venv'i ve modelleriyle birlikte); paylaşılan `bin\` klasörü (git/python/conda ikili
  dosyaları) silinmemeli — başka uygulamalar hâlâ ona bağımlı olabilir.
- Silinirse: "Kurulu yapay zeka uygulaması (programı, ortamı ve indirdiği modellerle birlikte)
  silinir; Pinokio mağazasından tekrar kurman gerekir."
- Kaynak: [Pinokio Desktop Docs](https://desktop.pinokio.co/docs/)

---

## Grup 2 — Oyun Platformları

| id | Platform | İçerik türü | Kütüphane/manifest kaynağı | Tipik boyut | Son oynama bilgisi |
|---|---|---|---|---|---|
| `steam` | Valve Steam | oyun | `steamapps\libraryfolders.vdf` + `appmanifest_<appid>.acf` | 60-150GB+ | Var — `LastPlayed` alanı (Unix epoch) |
| `epic-games` | Epic Games Launcher | oyun | `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON) | 40-120GB+ | Yok (yerel dosyada) |
| `ea-app` | EA app (Origin sonrası) | oyun | şifreli `%PROGRAMDATA%\EA Desktop\...\IS` + registry `HKLM\...\Origin Games\<ID>` | 30-100GB | Yok (doğrulanmadı) |
| `ubisoft-connect` | Ubisoft Connect | oyun | registry `HKLM\SOFTWARE\Wow6432Node\Ubisoft\Launcher\Installs\<ID>` | 50-150GB | Yok (doğrulanmadı) |
| `gog-galaxy` | GOG Galaxy | oyun | `%PROGRAMDATA%\GOG.com\Galaxy\storage\galaxy-2.0.db` + registry | 20-80GB | Var ama alan doğrulanmadı |
| `xbox-app` | Xbox app / Microsoft Store | oyun (UWP/MSIX) | Paket Yöneticisi API'si — dosya yok | 40-150GB | Yok |
| `battle-net` | Battle.net (Blizzard) | oyun | `%PROGRAMDATA%\Battle.net\Battle.net.config` (kısmen protobuf) | 30-100GB | Yok (doğrulanmadı) |
| `riot-client` | Riot Client (Valorant/LoL) | oyun | `%PROGRAMDATA%\Riot Games\RiotClientInstalls.json` | 20-30GB | Yok |

### `steam`
- Kütüphane konumları: `libraryfolders.vdf` (Valve KeyValue formatı) birden fazla sürücüdeki
  kütüphaneyi `"path"` alanlarıyla listeler.
- Birim/isim: her `appmanifest_<appid>.acf` bir oyun; **doğrulanmış alanlar**: `"name"`
  (görünen ad), `"installdir"` (klasör adı), `"SizeOnDisk"` (bayt), `"LastPlayed"` (Unix zaman
  damgası, 0 = hiç oynanmamış) — üç bağımsız parser kaynağı (`steamutils`, `vdfparse`,
  `python-steam-acf-parser`) bu alanı doğruladı.
- Silinirse: "Oyun dosyaları silinir; Steam kütüphanenden tekrar indirebilirsin. Kayıtlar
  genelde Steam Cloud'da veya ayrı bir klasörde tutulur."
- Kapalıyken güvenli mi: Evet, ama elle sadece `common\<installdir>` silinirse manifest
  yetim kalır — Steam'in kendi "Kaldır" seçeneğini kullanmak daha temiz.
- Kaynak: [GameFinder Wiki — Steam](https://github.com/erri120/GameFinder/wiki/Steam), [pinkwah/steam-appmanifest](https://github.com/pinkwah/steam-appmanifest/blob/master/README.md)

### `epic-games`
- Manifest alanları (doğrulanmış): `DisplayName`, `InstallLocation`, `InstallSize`, `AppName`.
  Son oynama alanı manifestte yok.
- Silinirse: "Oyun dosyaları silinir; Epic Games kütüphanenden tekrar indirebilirsin."
- Kapalıyken güvenli mi: Evet; launcher'ın kendi kaldırma seçeneğini tercih et, yoksa
  `.item` manifesti yetim kalabilir.
- Kaynak: [GameFinder Wiki — Epic](https://github.com/erri120/GameFinder/wiki/Epic-Games-Store)

### `ea-app`
- `IS` dosyası AES-256-CBC ile şifreli; `installInfos[]` içinde `baseInstallPath`, `baseSlug`,
  `installStatus` alanları var. Kayıt defterinde `HKLM\SOFTWARE\Wow6432Node\Origin Games\<ID>`
  yedek kaynak. Son oynama bilgisi yerel dosyada bulunamadı **(doğrulanmadı)**.
- Silinirse: "Oyun dosyaları silinir; EA app kütüphanenden tekrar indirebilirsin. Kayıtlar
  genelde bulutta veya Documents klasöründe."
- Kaynak: [GameFinder Wiki — EA Desktop](https://github.com/erri120/GameFinder/wiki/EA-Desktop), [filepathgeek.com](https://filepathgeek.com/posts/ea-app-games-location/)

### `ubisoft-connect`
- Kayıt defteri alt anahtarı doğru ama tam değer adı (`InstallDir` olduğu tahmin ediliyor)
  **(doğrulanmadı)**. Görevde bahsedilen `installs.yaml` dosyasının varlığı bu araştırmada
  teyit edilemedi — registry daha güvenilir kaynak.
- Silinirse: "Oyun dosyaları silinir; Ubisoft Connect'ten tekrar indirebilirsin."
- Kaynak: [Ubisoft Support — install location](https://www.ubisoft.com/en-gb/help/connectivity-and-performance/article/locating-your-installed-games-with-ubisoft-connect-pc/000065557)

### `gog-galaxy`
- `galaxy-2.0.db` (SQLite) içinde `InstalledBaseProducts` tablosu; tam şema doğrulanamadı.
  GOG oyunları DRM-free olduğu için silme sonrası tekrar indirme, hesap üzerinden offline
  installer olarak da yapılabilir. **GOG oyunları kayıtları kurulum klasörünün içinde tutma
  ihtimali diğer platformlardan daha yüksek** (PCGamingWiki genel gözlemi) — silmeden önce
  ekstra onay iste.
- Kaynak: [GameFinder Wiki — GOG Galaxy](https://github.com/erri120/GameFinder/wiki/GOG-Galaxy), [GOG Support](https://support.gog.com/hc/en-us/articles/213039625-Where-is-my-game-installed)

### `xbox-app`
- **Kritik kısıtlama**: `%PROGRAMFILES%\WindowsApps\` klasörü `TrustedInstaller` hesabına
  ait — normal kullanıcı hatta yönetici bile doğrudan silemez/taşıyamaz; ham dosya silme
  Windows Store kayıt veritabanını bozabilir. Kurulu oyunların bulunması bir hızlı işaretçi
  dosyası olan **`.GamingRoot`** (her sürücü kökünde, ör. `C:\.GamingRoot`) ile mümkün ama bu
  dosya oyun listesi vermez, sadece uygun sürücüleri işaretler.
- Doğru eylem: DustyBytes bu paketleri **PackageManager API'si veya `Remove-AppxPackage`**
  ile kaldırmalı; asla düz dosya silme/karantina akışına sokmamalı.
- Silinirse: "Oyun kaldırılır (Xbox app/Game Pass üzerinden); tekrar indirilebilir. Bazı yerel
  kayıtlar `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\` altında ayrıca kalabilir."
- Kaynak: [How-To Geek — .GamingRoot](https://www.howtogeek.com/872922/what-is-the-gamingroot-file/), [Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/4363316/cannot-clear-storage-after-uninstalling-games-from)

### `battle-net`
- `Battle.net.config` JSON+protobuf karışık; `Path` alanı içeren bir `InstallData` yapısı var
  ama tam üst seviye anahtar adı doğrulanamadı **(doğrulanmadı)**. Oyunlar
  `%PROGRAMFILES(X86)%\<Oyun Adı>` altına, Battle.net'in kendi klasörünün dışına kurulur.
  **World of Warcraft kayıtları kurulum klasörü içindeki `WTF`/`Interface` alt klasörlerinde
  tutulur** — silmeden önce özellikle uyar.
- Kaynak: [Playnite BattleNetGames.cs](https://github.com/JosefNemec/PlayniteExtensions/blob/master/source/Libraries/BattleNetLibrary/BattleNetGames.cs)

### `riot-client`
- `RiotClientInstalls.json` içinde `product_install_full_path` alanı doğrulandı; tam şema
  doğrulanamadı **(doğrulanmadı)**. Valorant ayrıca **Vanguard adında bir anti-hile
  sürücüsü/servisi** kurar — oyun klasörünü silmek bunu kaldırmaz, dokunulmamalı.
- Silinirse: "Oyun dosyaları silinir; hesap ilerlemesi tamamen sunucu tarafında olduğu için
  hiçbir şey kaybolmaz, Riot Client'tan tekrar indirebilirsin."
- Kaynak: [MiniTool — Valorant install path](https://www.minitool.com/news/how-to-change-valorant-install-path.html)

**Kesişen not:** Son oynama tarihi yalnızca Steam'de güvenilir biçimde doğrulandı (`LastPlayed`
alanı). Diğer platformlarda bu bilgi yoksa arayüzde "bilinmiyor" gösterilmeli, tahmin
üretilmemeli.

---

## Grup 3 — Sanallaştırma ve Emülasyon

⚠️ Bu grupta çoğu öğe için **doğrudan silme yanlış eylemdir** — dosya gerçek bir işletim
sistemi/kullanıcı verisi taşır. Tablo ve notlar doğru eylemi (araç-içi komut) ayrıca belirtir.

| id | Uygulama | İçerik türü | Varsayılan yol | Tipik boyut | Doğrudan silme uygun mu |
|---|---|---|---|---|---|
| `docker-desktop-wsl-data` | Docker Desktop (WSL2 arka uç) | sanal disk | `%LOCALAPPDATA%\Docker\wsl\data\ext4.vhdx` (ad sürümden sürüme değişebilir) | onlarca GB'a kadar sınırsız büyür | **Hayır** |
| `wsl-distro-vhdx` | WSL dağıtımı (Ubuntu, Debian, ...) | Linux disk imajı | `%LOCALAPPDATA%\Packages\<Paket>\LocalState\ext4.vhdx` | gerçek kullanım 2-30GB, tavan 256GB-1TB | **Hayır** |
| `hyperv-vm-disk` | Hyper-V sanal makinesi | sanal makine diski | `C:\Users\Public\Documents\Hyper-V\Virtual Hard Disks` | birkaç GB - 100GB+ | **Hayır** |
| `virtualbox-vm-disk` | Oracle VirtualBox VM | sanal makine diski | `%USERPROFILE%\VirtualBox VMs\<VM adı>\` | birkaç GB - 50GB+ | **Hayır** |
| `vmware-workstation-vm-disk` | VMware Workstation/Player VM | sanal makine diski | `%USERPROFILE%\Documents\Virtual Machines\` | birkaç GB - 100GB+ | **Hayır** |
| `android-avd` | Android Studio emülatörü (AVD) | Android sanal cihaz | `%USERPROFILE%\.android\avd\<isim>.avd\` | 2-10GB | Genelde evet |
| `android-sdk-system-image` | Android SDK sistem imajı | paylaşılan emülatör imajı | `%LOCALAPPDATA%\Android\Sdk\system-images\<api>\<abi>\` | 1-10GB | Evet, hiçbir AVD kullanmıyorsa |
| `genymotion-vm` | Genymotion masaüstü | Android sanal cihaz | `%LOCALAPPDATA%\Genymobile\Genymotion\deployed\<cihaz>\` | birkaç GB - 10GB | VirtualBox VM'i gibi davran |

### `docker-desktop-wsl-data`
- Sürümler arası tam alt klasör adı değişiyor (`data`/`disk`/`main`) — resmi Docker dokümanı
  kesin bir isim vermiyor; DustyBytes `%LOCALAPPDATA%\Docker\wsl\*\*.vhdx` üzerinde arama
  yapmalı, sabit yol varsaymamalı **(doğrulanmadı: kesin alt klasör adı)**.
- Özel konum: Docker Desktop → Settings → Resources → Advanced → "Disk image location";
  seçilen yol `%APPDATA%\Docker\settings-store.json` içinde saklanır.
- **Doğru eylem, ham silme değil**: önce `docker system prune -a --volumes` (Docker Desktop
  açıkken) ile kullanılmayan veriyi temizle; dosya hâlâ büyükse Docker Desktop'ın kendi
  "Ayarları sıfırla"/disk imajı sıfırlama seçeneğini kullan, ya da Docker ve WSL tamamen
  kapatılmışken `Optimize-VHD -Path <yol> -Mode Full` ile küçült.
- Silinirse (elle silinirse): "Tüm Docker imajları, konteynerleri ve birimleri (volume) kalıcı
  olarak kaybolur, hepsi sıfırdan çekilmeli/kurulmalı."
- Kaynak: [Docker Desktop Settings](https://docs.docker.com/desktop/settings-and-maintenance/settings/), [Hanselman — Shrink WSL2 disks](https://www.hanselman.com/blog/shrink-your-wsl2-virtual-disks-and-docker-images-and-reclaim-disk-space)

### `wsl-distro-vhdx`
- En güvenilir tespit: `HKCU:\Software\Microsoft\Windows\CurrentVersion\Lxss` kayıt defteri
  anahtarı — her dağıtımın `DistributionName` ve `BasePath` değerlerini verir (Microsoft'un
  kendi belgelediği yöntem, klasik `Packages\` yol tahmininden daha güvenilir).
- Birim/isim: `wsl --list --verbose` ile dağıtım listesi; her dağıtım `DistributionName`'i ile
  isimlendirilir.
- **Doğru eylem, ham silme değil**: `wsl --shutdown` ardından `wsl --unregister <dağıtım-adı>`
  — bu hem kaydı temizler hem vhdx'i siler, WSL iç durumunu bozmadan. Sadece alan kazanmak
  için (dağıtımı silmeden) `diskpart` ile `compact vdisk` veya `Optimize-VHD -Mode Full`
  kullanılmalı.
- Silinirse (elle silinirse, kayıt duruyorken): "WSL o dağıtımı bozuk/eksik disk olarak görür,
  hata verir" — bu yüzden asla ham silme önerilmemeli.
- Silinirse (doğru şekilde `--unregister` ile): "O Linux dağıtımının içindeki her şey (kurulu
  paketler, dosyalar) kalıcı olarak silinir."
- Kaynak: [Microsoft Learn — WSL disk space](https://learn.microsoft.com/en-us/windows/wsl/disk-space), [Microsoft Learn — WSL2 mount disk](https://learn.microsoft.com/en-us/windows/wsl/wsl2-mount-disk)

### `hyperv-vm-disk`
- Tespit: `Get-VM | Select VMId | Get-VHD | Select Path, FileSize, VhdType` (PowerShell,
  resmi Hyper-V modülü) — VM başına gerçek disk yolu ve boyutu.
- **Doğru eylem**: önce VM'in gerçekten istenmediğini doğrula, sonra `Remove-VM -Name <vm>
  -Force` ile Hyper-V veritabanından kaydı sil, ardından kalan disk dosyalarını temizle.
  Aktif kontrol noktası (checkpoint/`.avhdx`) olan bir diski asla tek başına silme — zincir
  kırılır ve VM bozulabilir.
- Silinirse: "O sanal makinenin işletim sistemi ve içindeki tüm veriler kalıcı olarak
  kaybolur."
- Kaynak: [Petri — Default Hyper-V storage paths](https://petri.com/default-hyper-v-storage-paths/), [Microsoft Learn — Get-VHD](https://learn.microsoft.com/en-us/powershell/module/hyper-v/get-vhd)

### `virtualbox-vm-disk`
- Tespit: `%USERPROFILE%\.VirtualBox\VirtualBox.xml` kayıtlı VM klasörünü ve VM listesini
  tutar; `VBoxManage list vms --long` ile per-VM disk yolu/boyutu alınır.
- **Doğru eylem**: `VBoxManage unregistervm "<VM adı>" --delete` — VirtualBox.xml'den kaydı
  siler ve tüm ilişkili dosyaları (snapshot dahil) temiz şekilde kaldırır. Sadece `.vbox`
  dosyasını silmek diski yetim bırakır, alan kazandırmaz.
- Silinirse: "O sanal makinenin işletim sistemi ve içindeki tüm veriler kalıcı olarak
  kaybolur."
- Kaynak: [VirtualBox Manual ch10](https://www.virtualbox.org/manual/ch10.html)

### `vmware-workstation-vm-disk`
- Özel konum: `%APPDATA%\VMware\preferences.ini` içinde `prefvmx.defaultVMPath` anahtarı.
- Birim/isim: her VM klasöründeki `.vmx` dosyasının `displayName` alanı; yoksa klasör adı.
- **Doğru eylem**: VMware Workstation'ın kendi "Remove from Library" / "Delete from Disk"
  seçeneğini kullan — elle silme, global envanter dosyasını (`%APPDATA%\VMware\inventory.vmls`)
  eksik dosyalara işaret eder halde bırakabilir.
- Silinirse: "O sanal makinenin işletim sistemi ve içindeki tüm veriler kalıcı olarak
  kaybolur."
- Kaynak: [VMware KB 1003880](https://kb.vmware.com/s/article/1003880)

### `android-avd`
- Özel konum: `ANDROID_AVD_HOME` ortam değişkeni; arama sırası `$ANDROID_AVD_HOME` →
  `$ANDROID_USER_HOME/avd/` → `$HOME/.android/avd/`.
- Birim/isim: her `<isim>.avd` klasörü bir birim; **insan-okunur isim**, klasördeki
  `config.ini` dosyasının `avd.ini.displayname` anahtarından alınır (ör. "Pixel 6 API 34").
- Silinirse: "O sanal Android cihazın kurulu uygulamaları ve verileri silinir — telefonu
  fabrika ayarlarına döndürmek gibi; kullandığı sistem imajı etkilenmez."
- Kapalıyken güvenli mi: Evet, emülatör/Android Studio tamamen kapalıyken; en temizi
  Android Studio'nun Device Manager'ından veya `avdmanager delete avd -n <isim>` ile silmek.
- Kaynak: [Android Developers — Managing AVDs](https://developer.android.com/studio/run/managing-avds), [Android Developers — command-line variables](https://developer.android.com/studio/command-line/variables)

### `android-sdk-system-image`
- Özel konum: `ANDROID_HOME` (güncel) veya eski `ANDROID_SDK_ROOT`; proje bazlı
  `local.properties` içinde `sdk.dir`.
- Birim/isim: `system-images\<api-seviyesi>\<tip>\<abi>\` klasör yolundan isim türet
  (ör. "Android 14 (API 34) — Google APIs, x86_64"). **Silmeden önce**: her AVD'nin
  `config.ini` dosyasındaki `image.sysdir.1` alanı bu imaja işaret ediyor mu kontrol et —
  ediyorsa o AVD imaj silindiğinde açılamaz hale gelir.
- Silinirse: "Paylaşılan sistem imajı silinir; onu kullanan sanal cihazlar tekrar
  indirilene kadar açılamaz."
- Kaynak: [Android Developers — command-line variables](https://developer.android.com/studio/command-line/variables)

### `genymotion-vm`
- Özel konum: Genymotion Desktop → Settings → Hypervisor sekmesi; tam config dosyası/anahtarı
  doğrulanamadı **(doğrulanmadı)**.
- **Doğru eylem**: Windows'ta varsayılan hipervizör VirtualBox olduğu için Genymotion cihazları
  aslında birer VirtualBox VM'idir — dosyayı elle silmek yerine Genymotion'ın kendi cihaz
  yöneticisinden sil, ya da VirtualBox tarafında `VBoxManage unregistervm --delete` kullan.
- Kaynak: [Genymotion Docs — Application](https://docs.genymotion.com/desktop/02_Application/)

---

## Grup 4 — Geliştirici Araç Önbellekleri

| id | Araç | İçerik türü | Varsayılan yol | Parçalama | Tipik boyut |
|---|---|---|---|---|---|
| `npm-cache` | npm | paket önbelleği | `%LocalAppData%\npm-cache` | Tek blok | Yüzlerce MB - birkaç GB |
| `pnpm-store` | pnpm | içerik-adresli paket deposu | `%LocalAppData%\pnpm\store` | Tek blok | Yüzlerce MB - birkaç GB |
| `yarn-cache` | Yarn (Classic/Berry) | paket önbelleği | `%LocalAppData%\Yarn\Cache` (Classic) | Tek blok | Yüzlerce MB - birkaç GB |
| `node-modules-stale-projects` | npm/pnpm/Yarn ekosistemi | proje bağımlılık ağacı | proje altında `node_modules\` (sabit kök yok) | **Proje başına** | 100MB - birkaç GB |
| `nuget-caches` | NuGet/.NET | paket + http önbelleği | `%userprofile%\.nuget\packages` + `%localappdata%\NuGet\v3-cache` | 4 ayrı blok | 1-10GB+ |
| `gradle-cache` | Gradle | bağımlılık + build önbelleği | `%USERPROFILE%\.gradle\caches\` | Tek blok | 1-10GB+ |
| `maven-local-repo` | Apache Maven | yerel artefakt deposu | `%USERPROFILE%\.m2\repository` | Tek blok | 1-5GB+ |
| `pip-cache` | pip (Python) | http + wheel önbelleği | `%LocalAppData%\pip\Cache` | Tek blok | Yüzlerce MB - birkaç GB |
| `conda-pkgs-cache` | conda | paylaşılan paket önbelleği | `<conda-kurulum-kökü>\pkgs` | Tek blok | 1-5GB+ |
| `uv-cache` | uv (Astral) | paket/build önbelleği | `%LOCALAPPDATA%\uv\cache` | Tek blok | Yüzlerce MB - birkaç GB |
| `cargo-registry-cache` | Cargo (Rust) | registry + kaynak önbelleği | `%USERPROFILE%\.cargo\registry\` | Tek blok (`bin\` hariç) | 1-10GB+ |
| `go-module-cache` | Go | modül kaynağı + derleme önbelleği | `%USERPROFILE%\go\pkg\mod` (+ `GOCACHE`) | Tek blok | Yüzlerce MB - birkaç GB |
| `unity-caches` | Unity | proje önbelleği + paylaşılan önbellek | proje altında `Library\` + `%LOCALAPPDATA%\Unity\cache\upm` | **Proje başına** + makine geneli | Library 1-20GB+ |
| `unreal-derived-data-cache` | Unreal Engine | proje türetilmiş veri + paylaşılan DDC | proje altında `DerivedDataCache\`/`Intermediate\`/`Saved\` + `%ProgramData%\Epic\Zen\Data` (UE5.4+) | **Proje başına** + makine geneli | Yüzlerce MB - birkaç GB proje başı; paylaşılan onlarca GB |

Bu grubun tamamı için ortak cümle geçerli: **"Hiçbir şey kaybolmaz; araç bir sonraki
kullanımda otomatik olarak tekrar indirir/derler, sadece o işlem daha yavaş olur."**
İstisnalar aşağıda ayrıca belirtildi.

### `npm-cache` / `pnpm-store` / `yarn-cache`
- Özel konum tespiti: `npm config get cache` (veya `.npmrc`'de `cache=`), `pnpm store path`,
  `yarn cache dir` (Classic) / `.yarnrc.yml`'de `cacheFolder` (Berry).
- npm'in Windows varsayım yolu resmi dokümanda `%LocalAppData%/npm-cache` yazıyor; bazı eski
  kaynaklar `%AppData%` diyor — DustyBytes sabit yol yerine `npm config get cache` çalıştırarak
  gerçek yolu okumalı.
- Kaynak: [npm Folders docs](https://docs.npmjs.com/cli/v11/configuring-npm/folders/), [pnpm Store settings](https://pnpm.io/settings/store), [Yarn Classic cache docs](https://classic.yarnpkg.com/lang/en/docs/cli/cache/)

### `node-modules-stale-projects`
- **Bu grupta gerçek istisna budur**: silinirse proje **çalışmaz hale gelir**, `npm install`
  (veya pnpm/yarn) yeniden çalıştırılmadan kullanılamaz.
- Bayatlık sezgisi: proje kök klasöründeki kaynak dosyaların (`.js/.ts/.tsx/...`,
  `node_modules` ve `.git` hariç) en yeni değişiklik tarihine, ya da varsa git `HEAD`
  commit tarihine bak; eşik olarak 60-90 gün öner, kullanıcı onayı olmadan silme.
- Kaynak: bir resmi standart yok, topluluk pratiği — [npkill aracı](https://github.com/voidcosmos/npkill)

### `nuget-caches`
- 4 ayrı yol, ortam değişkenleriyle taşınabilir: `NUGET_PACKAGES` (global-packages),
  `NUGET_HTTP_CACHE_PATH` (http-cache), `NUGET_SCRATCH` (temp), `NUGET_PLUGINS_CACHE_PATH`
  (plugins-cache). Gerçek çözümlenmiş yollar `dotnet nuget locals all --list` ile alınır.
- Kaynak: [Microsoft Learn — global packages and cache folders](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders)

### `gradle-cache`
- Özel konum: `GRADLE_USER_HOME` ortam değişkeni.
- Silmeden önce Gradle daemon'ını durdur (`gradle --stop`) — çalışırken silme, devam eden
  önbellek yazımını bozabilir.
- Kaynak: [Gradle — Directory Layout](https://docs.gradle.org/current/userguide/directory_layout.html)

### `maven-local-repo`
- Özel konum: `%USERPROFILE%\.m2\settings.xml` içinde `<localRepository>` etiketi.
- Kaynak: [Maven — Configuring Maven](https://maven.apache.org/guides/mini/guide-configuring-maven.html)

### `pip-cache`
- Özel konum: `PIP_CACHE_DIR` ortam değişkeni; gerçek yol `pip cache dir` ile okunur.
- Kaynak: [pip Caching docs](https://pip.pypa.io/en/stable/topics/caching/)

### `conda-pkgs-cache`
- Özel konum: `CONDA_PKGS_DIRS` ortam değişkeni veya `%USERPROFILE%\.condarc` içindeki
  `pkgs_dirs:` listesi.
- Uyarı: aktif bir ortam paket dosyalarını hardlink ile bu klasöre bağlıyor olabilir — ham
  klasör silme yerine `conda clean --all` tercih edilmeli, hâlâ referans verilen paketleri
  daha güvenli ayırt eder.
- Kaynak: [conda — custom env/pkg locations](https://docs.conda.io/projects/conda/en/stable/user-guide/configuration/custom-env-and-pkg-locations.html)

### `uv-cache`
- Özel konum: `UV_CACHE_DIR` ortam değişkeni veya `pyproject.toml`'de `tool.uv.cache-dir`.
- Kaynak: [uv — Caching concepts](https://docs.astral.sh/uv/concepts/cache/)

### `cargo-registry-cache`
- Özel konum: `CARGO_HOME` ortam değişkeni.
- Uyarı: `.cargo\bin\` klasörü önbellek değil, kurulu araçları (rustup toolchain'leri,
  `cargo install` hedefleri) barındırır — silme kapsamına dahil edilmemeli.
- Kaynak: [The Cargo Book — Cargo Home](https://doc.rust-lang.org/cargo/guide/cargo-home.html)

### `go-module-cache`
- Özel konum: `GOMODCACHE`, `GOPATH`, `GOCACHE` ortam değişkenleri; gerçek değerler
  `go env GOMODCACHE GOPATH GOCACHE` ile okunmalı, sabit yol varsayılmamalı.
- Kaynak: [Go Wiki — Modules](https://go.dev/wiki/Modules)

### `unity-caches`
- **Proje başına** birim, `node_modules` ile aynı mantık: proje klasörünün son değişiklik
  sinyaline (ör. `ProjectSettings/ProjectVersion.txt` mtime, en yeni `.cs`/`.unity` dosyası,
  ya da git commit tarihi) bakarak 60-90+ gündür dokunulmamış projeleri "durgun" işaretle.
- Silinirse: "Projenin kaynak varlıkları (Assets klasörü) etkilenmez; Unity projeyi bir
  sonraki açılışta yeniden içe aktarır — bu büyük projelerde uzun sürebilir (veri kaybı
  değil, sadece yavaşlık)."
- **Kritik uyarı**: proje Unity Editor'de açıkken `Library` klasörünü silme — bozulma riski
  var; sadece proje tamamen kapalıyken sil.
- Kaynak: [Unity Manual — Global cache](https://docs.unity3d.com/Manual/upm-cache.html), [Unity Discussions — Library folder safety](https://discussions.unity.com/t/is-it-safe-to-delete-library-folder-of-backed-up-project/811273)

### `unreal-derived-data-cache`
- Paylaşılan/DDC konum sürüme göre değişiyor: UE ≤5.3'te motor kurulumu içinde, UE 5.4+'ta
  Zen depolama ile `%ProgramData%\Epic\Zen\Data` — DustyBytes motor sürümünü tespit edip
  buna göre dallanmalı **(doğrulanmadı: her sürüm için kesin yol)**.
- Özel konum: `UE-SharedDataCachePath` ortam değişkeni (Windows'a özgü yazım); ini ayarı
  `DefaultEngine.ini`/`BaseEngine.ini` içinde `[DerivedDataBackendGraph]` bölümünün `Path=`
  parametresi.
- Silinirse: "Kaynak içerik (.uasset, .cpp, dokular) etkilenmez; Unreal türetilmiş veriyi
  (gölgelendirici, pişmiş veri) otomatik yeniden üretir — ilk açılış/derleme daha yavaş olur."
- Kaynak: [Epic Dev Docs — Using DDC](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-derived-data-cache-in-unreal-engine)

---

## Grup 5 — Medya, Yaratıcı Uygulamalar ve Mesajlaşma

| id | Uygulama | İçerik türü | Varsayılan yol | Tipik boyut | Kapalıyken güvenli mi |
|---|---|---|---|---|---|
| `premiere-pro-media-cache` | Adobe Premiere Pro | video düzenleme önbelleği | `%APPDATA%\Adobe\Common\Media Cache Files` | yüzlerce MB - onlarca GB | Evet |
| `after-effects-disk-cache` | Adobe After Effects | disk önbelleği | `%APPDATA%\Adobe\After Effects\Disk Cache` | 15-100GB (yapılandırılan tavana kadar) | Evet |
| `davinci-resolve-cacheclip` | DaVinci Resolve | render önbelleği | ilk medya depolama sürücüsünde `CacheClip\` (sabit yol yok) | 50-100GB, bazen 400GB+ | Evet |
| `obs-studio-recordings` | OBS Studio | ekran kaydı (orijinal içerik) | `%USERPROFILE%\Videos` | kayıt başına yüzlerce MB - birkaç GB | Evet, ama kalıcı silme |
| `nvidia-shadowplay-clips` | NVIDIA GeForce Experience / App | oyun kaydı (orijinal içerik) | `%USERPROFILE%\Videos` (oyun bazlı alt klasör) | klip başına onlarca MB - birkaç GB | Evet, ama kalıcı silme |
| `xbox-game-bar-captures` | Xbox Game Bar | ekran görüntüsü/kaydı (orijinal içerik) | `%USERPROFILE%\Videos\Captures` | görüntü birkaç MB, kayıt onlarca MB - birkaç GB | Evet, ama kalıcı silme |
| `iphone-local-backup` | Apple Devices / iTunes | telefon yedeği | `%APPDATA%\Apple Computer\MobileSync\Backup\<cihaz-id>` veya `C:\Users\<kullanıcı>\Apple\MobileSync\Backup\<cihaz-id>` | 1-100GB+ | **Dikkatli — asla otomatik silme** |
| `whatsapp-desktop-media` | WhatsApp Desktop | indirilen medya | `%LOCALAPPDATA%\Packages\5319275A.WhatsAppDesktop_cv1g1gvanyjgm\LocalState\shared\transfers` | yüzlerce MB - birkaç GB (doğrulanmadı) | Dikkatli |
| `telegram-desktop-downloads` | Telegram Desktop | indirilen dosya | `%USERPROFILE%\Downloads\Telegram Desktop` | doğrulanmadı | Dikkatli |

### `premiere-pro-media-cache`
- İki ayrı klasör: `Media Cache Files` (asıl önbellek dosyaları) ve `Media Cache` (veritabanı/
  index, `.cfa`/`.db`). Her ikisi de varsayılan olarak `%APPDATA%` (Roaming) altında — bazı
  kaynaklar yanlışlıkla `%LOCALAPPDATA%` diyor, doğrusu Roaming.
- Özel konum: Edit → Preferences → Media Cache içinde iki ayrı klasör seçici; bu ayar
  Premiere, After Effects, Media Encoder ve Prelude arasında paylaşılır. Programatik tespit
  için tam kayıt defteri/XML anahtarı doğrulanamadı **(doğrulanmadı)**.
- Birim/isim: her kaynak medya dosyasına ait `.mcdb`/`.cfa`/`.pek` çifti bir birim; isim
  genelde önbellek dosya adının içine gömülü orijinal dosya adından türetilir.
- Silinirse: "Önizleme/ses eşleme önbelleği silinir; Premiere projeyi bir sonraki açışta
  yeniden oluşturur (ilk açılış yavaş olur), orijinal görüntüye dokunulmaz."
- Kaynak: [Puget Systems — Storage/Cache Locations](https://www.pugetsystems.com/labs/articles/how-to-configure-storage-and-cache-file-locations-in-premiere-pro-2292/), [Adobe Help — Clear media cache](https://helpx.adobe.com/premiere/desktop/troubleshooting/media-issues/clear-media-cache-using-preferences.html)

### `after-effects-disk-cache`
- Varsayılan tavan: sürücü toplam boyutunun %10'u, en fazla 100GB.
- Özel konum: Edit → Preferences → Media & Disk Cache → "Choose Folder" ve "Maximum Disk
  Cache Size" alanı.
- Silinirse: "Önizleme kareleri silinir; After Effects bir sonraki önizlemede/render'da
  yeniden oluşturur, proje ve kaynak dosyalara dokunulmaz."
- Kaynak: [Adobe Help — Memory and storage](https://helpx.adobe.com/after-effects/using/memory-storage1.html)

### `davinci-resolve-cacheclip`
- Sabit bir varsayılan yol yok — Resolve, Preferences → Media Storage listesindeki **ilk**
  sürücüde `CacheClip` klasörü oluşturur; DustyBytes bu listeyi okuyup klasörü bulmalı
  **(doğrulanmadı: temiz kurulumda ilk varsayılan tam yol)**.
- Özel konum: Project Settings → Master Settings → Working Folders → "Cache Files Location".
- Silinirse: "Render önbelleği silinir; Resolve kaynak görüntüden gerektiğinde yeniden
  oluşturur, proje veritabanı ve orijinal medya etkilenmez."
- Kapalıyken güvenli mi: Evet, ama Resolve'un kendi Playback → Delete Render Cache → All
  seçeneği iç veritabanını daha tutarlı tutar.
- Kaynak: [Blackmagic Forum — Cache location](https://forum.blackmagicdesign.com/viewtopic.php?f=40&t=219785), [Microsoft Q&A — CacheClip](https://learn.microsoft.com/en-us/answers/questions/4351404/can-i-delete-cacheclip-folder-in-videos)

### `obs-studio-recordings` / `nvidia-shadowplay-clips` / `xbox-game-bar-captures`
- **Bu üçü önbellek değil, orijinal içerik** — silinirse geri getirilemez, başka hiçbir
  yerde kopyası yok. DustyBytes bunları "büyük ve yaşlı dosya" olarak göstermeli ama
  otomatik/varsayılan onayla silmemeli, kullanıcı her klibi tek tek onaylamalı.
- OBS özel konum: Settings → Output → "Recording Path".
- NVIDIA özel konum tespiti: kayıt defteri `HKEY_CURRENT_USER\SOFTWARE\NVIDIA
  Corporation\Global\ShadowPlay\NVSPCAPS` altında `DefaultPathW` değeri — arayüzü parse
  etmeden gerçek yolu okumanın yolu.
- Xbox Game Bar özel konum: klasör "Konum" sekmesinden taşınırsa
  `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders`
  kayıt defterine yazılır.
- Kaynak: [OBS file location guide](https://clip.dor.gg/en/blog/obs-recording-file-location), [NVIDIA Forums — save location](https://www.nvidia.com/en-us/geforce/forums/geforce-experience/14/182581/cant-change-save-location-for-shadowplay-clips/), [Microsoft Q&A — Game Bar captures](https://learn.microsoft.com/en-us/answers/questions/3277414/change-location-of-game-bar-captures)

### `iphone-local-backup`
- İki farklı yol, hangi uygulamanın yedeklediğine göre değişir: klasik iTunes (apple.com'dan)
  `%APPDATA%\Apple Computer\MobileSync\Backup\<cihaz-id>`; yeni "Apple Devices" Store
  uygulaması `C:\Users\<kullanıcı>\Apple\MobileSync\Backup\<cihaz-id>` — ikisi aynı makinede
  bir arada bulunabilir.
- Birim/isim: her `<cihaz-id>` klasörü bir yedek; klasördeki `Info.plist` dosyasından
  `Device Name`/`Display Name`, `Last Backup Date`, `Product Type` okunarak insan-okunur
  isim ve tarih üretilir.
- **Bu grubun en riskli öğesi**: yedek, önbellek değildir — silinirse ve iCloud'da veya
  başka bir yerde kopyası yoksa, telefonu o ana geri döndürme imkânı **kalıcı olarak
  kaybolur**. DustyBytes her yedeği cihaz adı + tarihle göstermeli ve tek tek onay istemeli,
  asla otomatik/toplu temizlemeye dahil etmemeli.
- Kaynak: [CopyTrans — backup location](https://www.copytrans.net/support/iphone-backup-location/), [Apple Support — locate backups](https://support.apple.com/en-us/108809), [The iPhone Wiki — backup structure](https://www.theiphonewiki.com/wiki/ITunes_Backup)

### `whatsapp-desktop-media` / `telegram-desktop-downloads`
- WhatsApp'ın Microsoft Store sürümü `%LOCALAPPDATA%\Packages\5319275A.WhatsAppDesktop_...`
  altında; doğrudan .exe sürümünün tam yolu bu araştırmada teyit edilemedi
  **(doğrulanmadı)**. WhatsApp'ta klasör-başına-sohbet yapısı yok, dosyalar tarih/tipe göre
  düz duruyor.
- Telegram özel konum: Settings → Advanced → "Download Path" — ama bu ayar düz metin bir
  dosyada değil, `tdata` klasöründeki şifreli/ikili ayar dosyasında tutuluyor; DustyBytes
  bunu programatik okuyamaz, sadece varsayılan `%USERPROFILE%\Downloads\Telegram Desktop`
  yolunu tarayabilir **(doğrulanmadı: özel konum programatik tespiti)**.
- Silinirse: her iki uygulama da bulut tabanlı olduğu için mesaj geçmişi sunucuda hâlâ
  duruyorsa medya genelde tekrar indirilebilir; ancak bu davranış her iki uygulama için de
  birincil kaynakla doğrulanamadı **(doğrulanmadı)** — kullanıcıya "muhtemelen tekrar
  indirilebilir, emin değiliz" şeklinde temkinli bir cümle önerilir.
- Kaynak: [blog.usro.net — WhatsApp data on PC](https://blog.usro.net/2024/10/where-whatsapp-data-is-stored-on-pc-a-guide-to-locating-your-chats-media-and-backups/), [mundobytes.com — Telegram downloads](https://mundobytes.com/en/Where-Telegram-desktop-downloads-and-files-are-stored/), [GitHub tdesktop#7788](https://github.com/telegramdesktop/tdesktop/issues/7788)

---

## Grup 6 — Windows Sistem Artıkları

| id | Ad | Yol | Tipik boyut | Doğrudan silme uygun mu |
|---|---|---|---|---|
| `windows-old-previous-version` | Windows.old | `%SYSTEMDRIVE%\Windows.old` | 15-30GB | **Kullanıcı onayıyla** — tek geri dönüş yolu |
| `crash-dumps` | MEMORY.DMP / Minidump | `%WINDIR%\MEMORY.DMP`, `%WINDIR%\Minidump\*.dmp` | DMP yüzlerce MB-birkaç GB; minidump ~256KB-2MB | Evet |
| `delivery-optimization-cache` | Delivery Optimization Cache | `%PROGRAMDATA%\Microsoft\Windows\DeliveryOptimization\Cache` | 1-20GB+ | Evet |
| `windows-installer-patchcache` | Windows Installer Cache / $PatchCache$ | `%WINDIR%\Installer`, `%WINDIR%\Installer\$PatchCache$` | 2-20GB+ | **HAYIR — özel algoritma gerekli** |
| `windows-update-download-cache` | SoftwareDistribution\Download | `%WINDIR%\SoftwareDistribution\Download` | 1-10GB | Evet |
| `downloads-folder-old-installers` | Downloads'taki eski kurulum dosyaları | `%USERPROFILE%\Downloads` (.exe/.msi/.iso/.zip, N günden eski) | değişken | Evet, yaş eşiğiyle |
| `nvidia-installer-cache` | NVIDIA kurulum önbelleği | `%PROGRAMDATA%\NVIDIA Corporation\Downloader`, eski `C:\NVIDIA` | 500MB-4GB+ | Evet |
| `amd-installer-cache` | AMD kurulum önbelleği | `%SYSTEMDRIVE%\AMD` | 500MB-birkaç GB | Evet |
| `winsxs-component-store` | WinSxS bileşen deposu | `%WINDIR%\WinSxS` | 5-15GB+ | **ASLA elle — sadece DISM** |
| `recycle-bin` | Geri Dönüşüm Kutusu | her sürücüde `$Recycle.Bin` | 0-onlarca GB | Kullanıcı onayıyla (zaten yarı-silinmiş) |
| `temp-folders` | Geçici dosyalar | `%TEMP%` + `%WINDIR%\Temp` | 500MB-10GB+ | Evet |
| `thumbnail-cache` | Küçük resim önbelleği | `%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db` | onlarca-birkaç yüz MB | Evet |
| `wer-report-queue` | Windows Hata Bildirimi kuyruğu | `%PROGRAMDATA%\Microsoft\Windows\WER\ReportQueue`, `...\ReportArchive` | değişken (NTFS sıkıştırmalı) | Evet |

### `windows-old-previous-version`
- Windows yükseltme/sıfırlama sonrası ~10 gün içinde otomatik siliniyor (60 güne kadar
  uzatılabilir, 10 günlük pencere geçmeden). Eşlik eden `$WINDOWS.~BT` ve `$WINDOWS.~WS`
  klasörleri de aynı yaşam döngüsüne sahip.
- Silinirse: "Önceki Windows sürümüne dönme imkânı kalıcı olarak kaybolur; disk alanı geri
  kazanılır." — DustyBytes bu klasörü **hiçbir zaman sessizce/varsayılan önerilerle**
  sunmamalı, kullanıcı geri dönmeyeceğinden emin olana kadar.
- Kaynak: [Microsoft Support — delete previous Windows version](https://support.microsoft.com/en-us/windows/delete-your-previous-version-of-windows-f8b26680-e083-c710-b757-7567d69dbb74)

### `crash-dumps`
- Silinirse: "Sistemin çalışmasını etkilemez; sadece geçmiş bir çökmenin (mavi ekran)
  nedenini araştırmak için kullanılırdı."
- Uyarı: kullanıcı tekrarlayan bir mavi ekranı aktif olarak araştırıyorsa (Microsoft Destek
  veya donanım üreticisiyle), silmeden önce sor — bu dosyalar tekrar üretilemez.
- Kaynak: [How-To Geek — remove memory dump files](https://www.howtogeek.com/728236/how-to-remove-system-error-memory-dump-files-on-windows-10/)

### `delivery-optimization-cache`
- Güncel yol `%PROGRAMDATA%\Microsoft\Windows\DeliveryOptimization\Cache` olarak doğrulandı;
  görevde adı geçen eski `SoftwareDistribution\DeliveryOptimization` yolu bu araştırmada
  Windows 10/11 için teyit edilemedi **(doğrulanmadı)** — DustyBytes her ikisini de taramalı.
- Silinirse: "Windows, güncellemeleri diğer cihazlarla eşler dosyası (peer-to-peer)
  paylaşımı için kullandığı önbelleği kaybeder; hiçbir kişisel veri kaybolmaz, gerektiğinde
  yeniden indirilir."
- Kaynak: [Microsoft Support — Delivery Optimization](https://support.microsoft.com/en-us/windows/delivery-optimization-in-windows-dbcaf188-0cf9-427a-b791-a7c5d740a48c)

### `windows-installer-patchcache` ⚠️
- **En riskli sistem öğesi.** Klasördeki `.msi`/`.msp` dosyaları rastgele adlandırılmış olsa
  da hâlâ kayıtlı yazılımların **onarım, değiştirme veya kaldırma** işlemleri için gerekli
  olabilir. Körlemesine "N günden eski dosyaları sil" kuralı burada **uygulanmamalı**.
- Doğru yöntem (PatchCleaner aracının kullandığı yaklaşım): önce `HKLM\SOFTWARE\
  Classes\Installer\Products` ve `Patches` kayıt defteri anahtarlarından (veya WMI
  `Win32_Product`'tan) hangi MSI/patch kayıtlarının hâlâ kullanımda olduğunu oku, sonra
  fiziksel dosyalarla karşılaştır — sadece **hiçbir kayıtla eşleşmeyen "yetim"** dosyaları
  aday göster.
- Silinirse (yanlışlıkla, hâlâ kayıtlıyken): "İlgili programın onarım/güncelleme/kaldırma
  işlemi bozulabilir, Windows eksik kaynak dosya isteyebilir."
- Öneri: bu kategoriyi ya otomatik temizlik dışında tut, ya da PatchCleaner tarzı çapraz
  kontrolü uygula ve varsayılan olarak karantinaya (kalıcı silme değil) yönlendir.
- Kaynak: [PatchCleaner](https://sourceforge.net/app/patch-cleaner/), [MakeUseOf — clean it safely](https://www.makeuseof.com/windows-installer-folder-clean-it-safely/)

### `windows-update-download-cache`
- Silinirse: "Windows, henüz ihtiyaç duyduğu güncellemeleri bir sonraki kontrolde yeniden
  indirir; hiçbir şey kaybolmaz."
- Kaynak: [Windows Central — clear SoftwareDistribution](https://www.windowscentral.com/how-clear-softwaredistribution-folder-windows-10)

### `downloads-folder-old-installers`
- Yaş eşiği: Windows'un kendi Storage Sense özelliği 1/14/30/60 gün seçenekleri sunuyor;
  DustyBytes için makul varsayılan **30 veya 60 gün** (60 gün, yanlış pozitifleri azaltmak
  için daha temkinli).
- Uzantı filtresi: `.exe`, `.msi`, `.msix`, `.iso`, `.zip`, `.7z`, `.rar`.
- Silinirse: "Kurulum dosyası silinir, zaten kurulmuş program etkilenmez; tekrar gerekirse
  üreticinin sitesinden yeniden indirilebilir." Uyarı: aynı uzantıyı taşıyan ama kurulum
  dosyası olmayan içerik (ör. kişisel fotoğraf zip'i) da bu filtreye takılabilir — silmeden
  önce kullanıcıya listeyi göster.
- Kaynak: [Microsoft Learn — Configure Storage Sense](https://learn.microsoft.com/en-us/windows/configuration/storage/storage-sense)

### `nvidia-installer-cache` / `amd-installer-cache`
- Tespit: sürüm numarası taşıyan alt klasörleri, kurulu sürücü sürümüyle (registry veya
  `nvidia-smi`) karşılaştır; eşleşmeyenler yetim/eski sürüm.
- Silinirse: "Kurulu sürücüyü etkilemez; sadece eski bir sürücüye geri dönme imkânı
  (yeniden indirmeden) ortadan kalkar."
- Kaynak: [How-To Geek — NVIDIA gigabytes](https://www.howtogeek.com/342322/why-does-nvidia-store-gigabytes-of-installer-files-on-your-hard-drive/), [Guiding Tech — AMD folder](https://www.guidingtech.com/amd-folder-getting-increasingly-large-what-to-do/)

### `winsxs-component-store` ⚠️
- **DustyBytes bu klasörün içeriğini asla taramamalı/tek tek silmemeli.** Tek desteklenen
  yol: `DISM /Online /Cleanup-Image /StartComponentCleanup` komutunu çalıştırıp DISM'in
  kendi geri kazandığı alanı raporlamak. Manuel dosya silme Windows Update'i, Sistem Geri
  Yükleme'yi bozabilir hatta önyükleme sorununa yol açabilir.
- Kaynak: [Microsoft Learn — Clean up WinSxS](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/clean-up-the-winsxs-folder?view=windows-11)

### `recycle-bin`
- Tespit: `SHQueryRecycleBinW` Win32 API'si her sürücü için toplam boyut/öğe sayısı verir —
  `$Recycle.Bin` klasörünü elle dolaşmaktan daha güvenilir.
- Bu öğe DustyBytes'ın kendi karantina felsefesiyle çelişir: Geri Dönüşüm Kutusu zaten
  "yarı silinmiş" bir alan — burayı boşaltmak ekstra bir onay adımı gerektirmeli, mevcut
  7 günlük karantina kuralına eşdeğer davranılmalı.
- Kaynak: [Microsoft Learn — SHQueryRecycleBinW](https://learn.microsoft.com/mt-mt/windows/win32/api/shellapi/nf-shellapi-shqueryrecyclebinw)

### `temp-folders` / `thumbnail-cache` / `wer-report-queue`
- Temp: hem `%USERPROFILE%\AppData\Local\Temp` hem `%WINDIR%\Temp` taranmalı (servisler
  ikincisine yazıyor); kilitli dosyalar atlanmalı, zorla kapatılmamalı.
- Thumbnail cache: Disk Temizleme'nin kendi yerleşik "Küçük Resimler" kategorisiyle aynı;
  her zaman güvenli.
- WER kuyruğu: klasörlerin **içeriği** silinmeli, `ReportQueue`/`ReportArchive` klasörlerinin
  kendisi değil; yönetici izni gerektirir.
- Kaynak: [Wikipedia — Temporary folder](https://en.wikipedia.org/wiki/Temporary_folder), [Wikipedia — Windows thumbnail cache](https://en.wikipedia.org/wiki/Windows_thumbnail_cache), [Windows OS Hub — WER queue](https://woshub.com/wer-windows-error-reporting-clear-reportqueue-folder-windows/)

---

## Grup 7 — Pazar Taraması: Rakipler Ne Sunuyor, Ne Kaçırıyor

**WizTree** — MFT'den okuyarak saniyeler içinde treemap ve ağaç listesi çıkarır; en büyük
dosya/klasörleri boyuta göre sıralar. Silmeden önce sonucu açıklamaz, ne olduğunu
isimlendirmez — "Steamapps/common" gibi ham yol gösterir. Silme doğrudan Explorer/Geri
Dönüşüm Kutusu üzerinden, kalıcı silme opsiyonu da var; AI modeli, oyun kurulumu gibi
kavramları tanımıyor.

**WinDirStat** — Aynı treemap + uzantı listesi deseni, açık kaynak, MFT okumadığı için
yavaş. Kopya dosya tespiti (hash) var ama "bu ne, silinirse ne olur" açıklaması yok. Silme
kalıcı; geri alma yok.

**TreeSize (JAM Software)** — Treemap + kategorize arama: "yinelenen dosyalar", "geçici
dosyalar", bulut sürücüleri ayrı görülüyor. Kurumsal sürümde NTFS hardlink ile "dedupe"
seçeneği var (silmeden alan kazanma), yani kısmen tersinir bir yol sunuyor; ama uygulama-
özel tanıma (LM Studio, Docker, VM diski) yok, sonuç yine dosya yolu ve boyut.

**SpaceSniffer** — Saf treemap, taramayı bitmeden canlı gösterir, filtre/etiketleme var.
Hiçbir açıklama veya güvenli silme katmanı yok; kullanıcı bloğu görüp klasör adından ne
olduğunu kendisi çıkarmak zorunda.

**CCleaner (Health Check / Smart Cleaning)** — Treemap değil, kategorize liste: gizlilik/
alan/hız/güvenlik dörtlüsü, "tarayıcı önbelleği", "sistem geçici dosyaları" gibi genel
etiketler kullanır — tam olarak şikayet edilen "600MB önbellek" paternini üretiyor. Silme
varsayılan olarak kalıcı (Geri Dönüşüm Kutusu'na göndermiyor), tüketici uygulama verisi
(model, VM, dev cache) tanımıyor.

**BleachBit** — "Cleaner" kuralları uygulama bazlı (Firefox, Chrome, LibreOffice, Spotify...)
ama yine genel "önbellek/log/geçmiş" siler, GB seviyesinde tek bir büyük öğeyi ismiyle öne
çıkarmaz. Önizleme sunar (silinecekler listelenir) fakat sonuç cümlesi yok; "shred"
seçeneğiyle geri dönüşü olmayan, adli olarak kurtarılamaz silme sunuyor — quarantine'in tam
tersi bir felsefe.

**Windows 11 Storage Sense / Cleanup Recommendations** — Kategorilere ayırır: geçici
dosyalar, büyük/kullanılmayan dosyalar, buluta senkronize dosyalar, kullanılmayan
uygulamalar. "Kullanılmayan uygulama" tespiti var (isimle: "X uygulamasını 90 gündür
kullanmadınız") ama dosya seviyesinde AI modeli/dev cache/VM diski ayrımı yok. Silme/
online-only dönüştürme geri dönüşü zor; Geri Dönüşüm Kutusu'nu da otomatik boşaltabiliyor.

**CleanMyPC (Windows, MacPaw, 2024'te durduruldu)** — Genel junk/registry/startup
temizliği, kategorize liste biçiminde; Shredder (kalıcı, kurtarılamaz silme) sunuyor ama
"büyük & eski dosyalar" tarzı akıllı gruplama yok, güncelleme de almıyor artık.

**CleanMyMac X — Large & Old Files (en çok atıf alan örnek)** — Bu, incelenen araçlar
içinde hedef pattern'e en yakın olan: dosyaları boyut + erişim tarihine göre gruplar ("Bir
Yıl Önce", "Arşivler" gibi klasörler), 50MB üstünü filtreler. Yine de dosya *türünü*
isimlendirir, uygulamanın *anlamını* açıklamaz (örn. "Xcode simulator: 12GB kullanılmayan
simülatör" demez, sadece "Archives, 12GB" der); silme seçenekleri arasında "Taşı: Çöp
Kutusu" (tersinir) var ama varsayılan "Remove Immediately" kalıcı silme.

**Wise Disk Cleaner** — Common/Advanced Cleaner ayrımıyla kategorize tarama (geçici,
tarayıcı, sistem günlüğü, Windows update artığı), "System Slimming" ile nadir kullanılan
Windows bileşenlerini bulur. Tamamen genel kategori bazlı; hiçbir uygulama-özel
isimlendirme yok, silme kalıcı.

**Glary Utilities** — Junk temizliği + kayıt defteri + kopya bulucu + disk analizini tek
panelde birleştirir, "1-Click Maintenance" ile toplu temizlik yapar. İncelemeler açıkça
uyarıyor: aşırı temizlik sistem geri yükleme noktalarını silebiliyor — yani sonuç açıklaması
eksikliği gerçek hasara yol açmış bir örnek. Uygulama-özel/AI model/VM tanıma yok, silme
kalıcı.

### Sentez

On araçtan hiçbiri "bu 40GB'lık klasör LM Studio'nun indirdiği dil modelleri" gibi
uygulama-farkında, düz dilde bir isimlendirme yapmıyor — hepsi ya ham treemap/yol (WizTree,
WinDirStat, SpaceSniffer) ya da genel kategori etiketi ("tarayıcı önbelleği", "geçici
dosyalar" — CCleaner, Wise, Glary, Storage Sense) sunuyor. Silinirse ne olacağını tek
cümleyle açıklayan hiçbir araç yok; en yakını CleanMyMac'in tarih/tür gruplaması ama o da
anlam değil sadece meta veri veriyor. Tersinirlik tarafında tablo daha da kötü: çoğu
(CCleaner, BleachBit, Wise, Glary, WinDirStat) kalıcı silme yapıyor, BleachBit hatta adli
kurtarmayı engelleyen "shred" sunuyor; sadece CleanMyMac'in "Çöp Kutusu'na taşı" seçeneği ve
TreeSize'ın hardlink-dedupe'u kısmi bir güvenlik ağı sağlıyor — 7 günlük otomatik karantina
kavramı hiçbirinde yok. AI modeli, oyun kurulumu, dev cache (node_modules, Docker layer'ları)
veya VM disk imajı gibi büyük ve anlamlı tüketicileri isimle tanıyan tek araç yok; Storage
Sense'in "kullanılmayan uygulama" tespiti en yakın örnek ama dosya seviyesine inmiyor. Bu,
"büyük şeyi düz dilde adlandır + tek tıkla 7 günlük karantina" yaklaşımının hem anlama hem
güven ekseninde net bir boşluğu doldurduğunu gösteriyor: kullanıcı ne sildiğini anlıyor,
pişman olursa geri alabiliyor — pazardaki hiçbir araç bu ikisini birlikte sunmuyor.

**Kaynaklar:**
- WizTree: https://diskanalyzer.com/ , https://wiztree.world/
- WinDirStat: https://github.com/windirstat/windirstat
- TreeSize: https://www.jam-software.com/treesize/find-remove-duplicate-files.shtml , https://www.jam-software.com/treesize/features.shtml
- SpaceSniffer: https://spacesniffer.info/
- CCleaner Health Check: https://www.ccleaner.com/ccleaner/health-check , https://support.ccleaner.com/s/article/what-is-health-check
- BleachBit: https://www.bleachbit.org/features , https://docs.bleachbit.org/cml/cleanerml.html
- Windows 11 Storage Sense: https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/free-up-drive-space-in-windows
- CleanMyPC: https://www.digitalcitizen.life/cleanmypc-review/
- CleanMyMac X Large & Old Files: https://macpaw.com/cleanmymac-x/large-and-old-files , https://macpaw.com/support/cleanmymac/knowledgebase/large-and-old
- Wise Disk Cleaner: https://www.wisecleaner.com/wise-disk-cleaner.html
- Glary Utilities: https://www.glarysoft.com/how-to/10-glary-utilities-features-for-better-windows-disk-cleanup-and-optimization-management/

---

## Kapsam Dışı Bırakılan / Ek Bulunan Öğeler

Görev metninde açıkça istenmemiş ama araştırma sırasında ortaya çıkan, ileride eklenmesi
makul iki öğe not düşüldü:

- **Windows Update WinSxS DISM temizliği** ve **Delivery Optimization** zaten Grup 6'da.
- Chrome/Edge/Firefox tarayıcı profili önbellekleri kasıtlı olarak dışarıda bırakıldı —
  görev metni "600MB uygulama önbelleği" örneğini zaten düşük öncelikli saydığı için, bu tür
  klasik "genel önbellek" kategorisi DustyBytes'ın farklılaşmak istediği alan değil.

## Genel Uygulama Notları (Tüm Gruplar İçin)

- **Üç risk katmanı** öner: (1) düz silinebilir/karantinaya uygun — çoğu AI model, dev
  cache, medya önbelleği; (2) "doğru araçla sil" — Docker/WSL/Hyper-V/VirtualBox/VMware/
  Xbox paketleri, asla ham dosya silme değil; (3) "asla otomatik önerme" — Windows.old,
  iPhone yedekleri, WinSxS, $PatchCache$.
- Son oynama tarihi yalnızca Steam'de güvenilir; diğer platformlarda "bilinmiyor" göster.
- Birçok özel-konum tespiti bu araştırmada **(doğrulanmadı)** işaretli kaldı (LM Studio'nun
  tam config anahtarı, Pinokio'nun config.json anahtarı, WhatsApp'ın .exe sürümü yolu,
  Telegram'ın programatik yol okuma imkânsızlığı, Genymotion'ın dosya biçimi) — bu noktalar
  koda geçmeden önce gerçek bir Windows makinesinde tek tek doğrulanmalı.

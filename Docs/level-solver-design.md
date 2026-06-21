# Twisted Tangle — Oyun Kuralları & Solver Tasarımı

> Amaç: Bir level'in **çözülebilir mi** olduğunu ve **zorluğunu** otomatik ölçen bir solver.
>
> **İş akışı (editör-zamanı, designer güdümlü):** designer editörde AI'a bir level ürettirir → solver
> editörde **çözülebilirliği** ve **zorluğu** gösterir → designer düzenler → **commit** eder.
> ❌ Runtime'da level üretimi YOK (asla). ❌ Toplu/hızlı (rapid) üretim şimdilik kapsam dışı.
> Solver **editörde** çalışan bir tooldur; oyun çalışırken bu doc'u veya solver'ı kimse çağırmaz.
> Bu doc'un tek tüketicisi: solver'ı kuracak ajan (gelecek oturumlar) + designer/geliştirici.
>
> Bu döküman **mantığı oturtmak** içindir; kod sonra gelir. Notasyon: ✅ onaylı, ⚠️ açık.

> **Model kararı:** **Planarity + 3-birim uzama limiti** (layer = strateji, kural değil).
> Bir pini, kilitli değilse, **erişim içindeki** boş bir deliğe taşıyarak çözersin. İki **sert** kural + bir **tercih**:
> 1. **Uzama (sert):** her bağlı rope'un pinA–pinB uzunluğu **≤ 3** (yön önemsiz → **Chebyshev/kare**).
> 2. **Katı halat YOK (sert):** sürerken ip ipi bloke etmez; sadece **son kesişim** çözülmeyi belirler.
> 3. **Layer tercihi (yumuşak):** alttaki rope da oynatılabilir; ama çözüme **genelde en üstten başlanır** →
>    solver eşit verimlilikteki hamlelerde **yüksek layer'lı (üstteki) rope'u önce** seçer. **Yasak değil, sıralama.**
> Uzağa taşımak pinleri sırayla oynatmayı (**inchworm**) gerektirebilir.
> (Over/under = global `Layer`, eşitlikte rope id; per-crossing override'lar şimdilik yok sayılıyor.)

---

## 1. Sözlük

| Terim | Anlam |
|---|---|
| **Delik (hole/slot)** | Grid üzerindeki pin yuvası (oyundaki soluk daireler). Pinler sadece deliklerde durur. |
| **Pin (node)** | Bir delikteki düğüm. Bir veya daha çok rope'un ucu olabilir. |
| **Rope (edge)** | İki pini (pinA, pinB) birleştiren **düz** çizgi. |
| **Crossing** | İki rope segmentinin kesişmesi. |
| **Çözülmüş level** | Hiçbir rope başka bir rope'u kesmiyor. |

---

## 2. Oyun Kuralları

### 2.1 Temel
- ✅ Pinler ayrık **deliklerde** durur. Bir rope tam **iki** pini bağlar.
- ✅ Bir pin birden çok rope'a ait olabilir (Octopus pin = derece ~2; genelde de sınırlı derece).
- ✅ Rope iki pini arasında **düz çizgi** segmentidir.
- ✅ **pinA/pinB ayrımı yalnızca etiket** (çizim sırası: Path'in ilk/son ucu). Oynanışta **simetrik**;
  solver iki ucu eşit görür → ayrı bir A/B authoring yapısı **kurulmadı.** **Rope-merkezli** bakıyoruz:
  bir rope **hiçbir ipi kesmiyorsa çözülmüştür** (nailed/kilitli pin olsa da bu tanım değişmez).

### 2.2 Hareket
- ✅ Hamle = bir ipin **ucunu (pini)** tut, **erişim içindeki** boş bir deliğe bırak; pin oraya bağlanır.
- ✅ **Uzama limiti VAR:** bir hamle, o pine bağlı **her** rope'un pinA–pinB uzunluğunu **≤ 3** tutmalı.
  Yön önemsiz → **Chebyshev (kare):** `max(|dx|,|dy|) ≤ 3`. (Metrik tek satırda Öklid/Manhattan'a çevrilebilir.)
- ✅ **Layer = tercih, kural değil:** alttaki rope da oynatılabilir. Ama çözüme **genelde en üstten başlanır** →
  solver eşit verimlilikteki hamlelerde **yüksek layer'lı rope'u önce** dener (sıralama/tiebreak). Over/under = global `Layer`, eşitlikte rope id.
- ✅ **Katı halat YOK** — hareket sırasında hiçbir ip diğerini geometrik bloke etmez; sadece **son durumdaki kesişim** çözülmeyi belirler.
- ✅ Atomik kural: bir hamle tek bir pini taşır. Uzağa taşımak için pinler sırayla yürütülür (**inchworm**).

### 2.3 Kazanma & Sınır
- ✅ **Kazanma:** hiçbir rope başka rope'u kesmiyor (tümü ayrık).
- ✅ **Sınır:** hamle sayısı (bu projede **süre** — UI tercihi). Biterse başarısız.
- ⚠️ Süre ↔ hamle eşlemesi: solver **min hamle** hesaplar; level'in süre/hamle bütçesi bu sayıdan türetilir.

### 2.4 Kilitli (locked / fixed) entity'ler — ÇEKİRDEK
- **Kilitli pin oynatılamaz**; konumu sabittir (solver aramasında hareket etmez).
- Designer bunları **bağlam/çapa** olarak yerleştirir: "şu x, y, z kilitli; level'i bunların etrafında kur."
  AI üretimi kilitli pinleri **veri olarak alıp onlara göre** üretir; solver onları **sabit düğüm** sayar.
- Çözülebilirliği doğrudan etkiler: kilitli pinler kaçınılmaz kesişime zorlarsa level çözülemez olabilir.
- Somut güncel biçim: **needle (iğneli) pin** = hareketi tamamen kısıtlanmış pin; bir zorluk modifiyesi. İleride başka hareket-kısıtı feature'ları gelecek; solver bunu genel "oynar/oynamaz" olarak modelliyor (ileride per-pin "izinli hücreler"e genişletilebilir).
- ⚠️ **Temsil:** PegData'da henüz `Locked` alanı yok. Öneri: per-peg `bool Locked`. Solver şimdilik
  kilitli hücreleri **parametre** olarak alır (veri-modeli kararından bağımsız).

### 2.4b Diğer özel pinler (sonraki faz)
- **Octopus pin:** birden çok rope taşır (derece 2). Normal düğüm gibi işlenir.
- **Key/Lock:** anahtarın ipleri çözülünce kilit açılır → **sıralama** kısıtı.
- **Barrier:** statik engel; bir rope bunun üzerinden geçemez → kesişim testine "yasak bölge" olarak eklenir.

---

## 3. Formal Model

### 3.1 Durum (state)
```
State = pin → delik ataması   // hangi pin hangi delikte
```
Ropelar (kenarlar) sabittir; sadece pin konumları değişir. Crossing'ler konumlardan **türetilir**.
Tekrar aramayı önlemek için durum **hash**'lenir.

### 3.2 Hamle (move)
- **Kilitli olmayan** bir pini boş bir deliğe taşı — yalnızca o pine bağlı **tüm ropelar ≤ 3** kalıyorsa. Kilitli pinler taşınamaz.

### 3.3 Hedef
- Kesişim kümesi boş.

---

## 4. Türetilen Kolaylaştırıcı Özellikler
- ✅ **Düz ip ⇒ çift başına en fazla 1 crossing** (iki doğru parçası en çok bir kez kesişir).
- ✅ Tangle = bir **ikili-kesişim kümesi** (düğüm teorisi değil).
- ✅ Bol boş delik ⇒ planar bir graf için kesişimsiz düzen neredeyse her zaman var; zorluk **min hamle**de.

---

## 5. Solver Mimarisi

### 5.0 Atomik işlem
- **Segment–segment kesişim testi.** "Bu rope birini kesiyor mu?" = bu testin tüm ropelara karşı çağrılması.

### 5.1 Katman 1 — Çözülebilir mi? (varış düzeni var mı)
- **Hızlı eleme:** ip grafiği **planar değilse** (K5 / K3,3 minörü) hiçbir düzen kesişimsiz olamaz ⇒ **kesin çözülemez.** Planarity testi lineer zamanda yapılır.
- Planar **ve** yeterli boş delik varsa ⇒ kesişimsiz bir hedef düzen bulunabilir.
- ⚠️ Tam titizlik: hedef, pinlerin **sabit delik kümesine** düz-çizgi gömülmesidir (point-set embedding). Pratikte bol delikle sorun olmaz; gerekirse hedef düzeni arama ile bulunur.

### 5.2 Katman 2 — Bütçe içinde mi? (min hamle araması)
```
Durum  = pin→delik ataması
Hamle  = bir oynar pini, tüm bağlı ropeları ≤3 tutan boş bir deliğe taşı (Chebyshev)
Hedef  = kesişim yok
Maliyet= hamle sayısı
```
- **En-iyi-öncelikli / A\*** ile ara; sezgi `h = crossing_sayısı` (veya kesişime karışan pin sayısı).
- **Hamle önceliklendirme:** en çok kesişen pini, en çok kesişimi gideren boş deliğe taşıyan hamleler önce.
- **Görülen-durum tablosu** ile tekrarı atla.
- **Bütçe sınırı:** N genişlemede çözülemezse "bu bütçede çözülemez" (üretim filtresi için yeterli).
- Sadece **kesişime karışan** pinleri oynatmak yeterli ⇒ dallanma küçülür.

### 5.3 Çıktılar
1. **Çözülebilir mi?** = planar mı **ve** bütçe içinde min hamle bulundu mu.
2. **Zorluk skoru:** min hamle + kesişim yoğunluğu + (özel pinler eklenince) sıralama/engel zorlukları.

---

## 6. Zorluk Metrikleri (taslak)
| Sinyal | Anlamı |
|---|---|
| Min hamle sayısı | Temel efor |
| Başlangıç crossing sayısı/yoğunluğu | Görsel + mantıksal karmaşa |
| Dallanma (adım başı seçenek) | Yanlış yapma kolaylığı |
| (Key/Lock) sıralama derinliği | Bağımlı açma zincirleri |
→ Mevcut editör difficulty metrikleriyle hizalanmalı.

---

## 7. Kodlamadan Önce Kapatılacak Açık Noktalar (⚠️)
1. **Süre ↔ hamle bütçesi eşlemesi** (§2.3).
2. **Point-set embedding titizliği** (§5.1): bol delikle pratikte gerek olmayabilir; doğrulanmalı.
3. **Boş delik kuralı:** pin yalnızca boş deliğe mi taşınır (takas yok)? (Varsayım: evet.)
4. **Diğer özel pinlerin** ilk sürümde kapsamda olup olmadığı (§2.4b). Öneri: önce çekirdek + kilitli pin, sonra Octopus/Key-Lock/Barrier.
5. **Difficulty eşlemesi** (§6).
6. **Kilitli-lik temsili** (§2.4): PegData'ya per-peg `Locked` flag mı, yoksa "fixed" entity tipi mi? Solver bu karardan bağımsız (locked set'i parametre alır).
7. **Rope = düz kenar varsayımı (A):** RopeData aslında **polyline** (ara peg'lerde sarım). Solver her rope'u ilk↔son peg arası **düz kenar** sayar; ara waypoint'ler yok sayılır (başlangıç görseli). Sarımlar oynanışı etkileyecekse yeniden ele alınır.

---

## 8. Kodlama Sırası
1. Veri: pin/delik/rope'tan solver iç modeline (graf) dönüştürücü.
2. Segment-kesişim + "rope birini kesiyor mu" + tüm crossing kümesi.
3. Planarity testi (Katman 1 — imkânsızı ele).
4. Katman 2: A* min-hamle araması (+ görülen-durum + bütçe).
5. Çıktı: çözülebilirlik + zorluk; editöre "Solve / Score" düğmesi.
6. (Sonra) Özel pinler: Fixed, Octopus, Key/Lock, Barrier.

---

## 9. Kaynaklar (gerçek oyun araştırması)
- Rollic Twisted Tangle — App Store: https://apps.apple.com/us/app/twisted-tangle/id6447757125
- Twisted Tangle — CrazyGames: https://www.crazygames.com/game/twisted-tangle-nmt
- Rollic Help Center — How to play: https://rollic.helpshift.com/hc/en/11-twisted-tangle/faq/254-how-to-play-twisted-tangle/

Özet bulgu: "Bir ipin ucunu tutup başka bir deliğe sürüklersin, eski delikten kopup yenisine bağlanır; amaç hiçbir ipin kesişmemesi; sınır hamle/süre." Hiçbir kaynakta uzunluk limiti veya katı-halat kuralı geçmiyor.

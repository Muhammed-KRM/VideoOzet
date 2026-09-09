using Microsoft.EntityFrameworkCore;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Seeds;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        // ── Şehirler ─────────────────────────────────────────
        var cityCount = context.Cities.Count();
        if (cityCount < 81)
        {
            var existingNames = context.Cities.Select(c => c.Name).ToHashSet();
            var allCities = GetCities();

            // Eksik şehirleri ID olmadan ekle (sequence otomatik atar)
            var missingCities = allCities
                .Where(c => !existingNames.Contains(c.Name))
                .Select(c => new City { Name = c.Name, Slug = c.Slug, PlateCode = c.PlateCode })
                .ToList();

            if (missingCities.Count > 0)
            {
                await context.Cities.AddRangeAsync(missingCities);
                await context.SaveChangesAsync();
            }
        }

        // ── İlçeler ──────────────────────────────────────────
        // İlçesi olmayan şehirlere ilçe ekle
        var plateToDistricts = GetDistrictsByPlate();
        var allDbCities = context.Cities.AsNoTracking().ToList();
        var existingDistrictCityIds = context.Districts
            .Select(d => d.CityId).Distinct().ToHashSet();

        var citiesNeedingDistricts = allDbCities
            .Where(c => !existingDistrictCityIds.Contains(c.Id))
            .ToList();

        if (citiesNeedingDistricts.Count > 0)
        {
            var newDistricts = new List<District>();
            foreach (var city in citiesNeedingDistricts)
            {
                if (plateToDistricts.TryGetValue(city.PlateCode, out var districtNames))
                {
                    foreach (var name in districtNames)
                        newDistricts.Add(new District { CityId = city.Id, Name = name, Slug = Slugify(name) });
                }
            }
            if (newDistricts.Count > 0)
            {
                await context.Districts.AddRangeAsync(newDistricts);
                await context.SaveChangesAsync();
            }
        }

        // ── Branşlar ─────────────────────────────────────────
        var branchCount = context.Branches.Count();
        if (branchCount < 10)
        {
            var existingBranchNames = context.Branches.Select(b => b.Name).ToHashSet();
            var allBranches = GetBranches();
            var missingBranches = allBranches
                .Where(b => !existingBranchNames.Contains(b.Name))
                .Select(b => new Branch
                {
                    Name = b.Name, Slug = b.Slug, Category = b.Category,
                    IsPopular = b.IsPopular, DisplayOrder = b.DisplayOrder
                })
                .ToList();

            if (missingBranches.Count > 0)
            {
                await context.Branches.AddRangeAsync(missingBranches);
                await context.SaveChangesAsync();
            }
        }
    }

    // ════════════════════════════════════════════════════════
    // ŞEHİRLER — Türkiye 81 İl
    // ════════════════════════════════════════════════════════
    private static List<City> GetCities() => new()
    {
        new() { Id=1,  Name="Adana",           Slug="adana",           PlateCode=1  },
        new() { Id=2,  Name="Adıyaman",        Slug="adiyaman",        PlateCode=2  },
        new() { Id=3,  Name="Afyonkarahisar",  Slug="afyonkarahisar",  PlateCode=3  },
        new() { Id=4,  Name="Ağrı",            Slug="agri",            PlateCode=4  },
        new() { Id=5,  Name="Amasya",          Slug="amasya",          PlateCode=5  },
        new() { Id=6,  Name="Ankara",          Slug="ankara",          PlateCode=6  },
        new() { Id=7,  Name="Antalya",         Slug="antalya",         PlateCode=7  },
        new() { Id=8,  Name="Artvin",          Slug="artvin",          PlateCode=8  },
        new() { Id=9,  Name="Aydın",           Slug="aydin",           PlateCode=9  },
        new() { Id=10, Name="Balıkesir",       Slug="balikesir",       PlateCode=10 },
        new() { Id=11, Name="Bilecik",         Slug="bilecik",         PlateCode=11 },
        new() { Id=12, Name="Bingöl",          Slug="bingol",          PlateCode=12 },
        new() { Id=13, Name="Bitlis",          Slug="bitlis",          PlateCode=13 },
        new() { Id=14, Name="Bolu",            Slug="bolu",            PlateCode=14 },
        new() { Id=15, Name="Burdur",          Slug="burdur",          PlateCode=15 },
        new() { Id=16, Name="Bursa",           Slug="bursa",           PlateCode=16 },
        new() { Id=17, Name="Çanakkale",       Slug="canakkale",       PlateCode=17 },
        new() { Id=18, Name="Çankırı",         Slug="cankiri",         PlateCode=18 },
        new() { Id=19, Name="Çorum",           Slug="corum",           PlateCode=19 },
        new() { Id=20, Name="Denizli",         Slug="denizli",         PlateCode=20 },
        new() { Id=21, Name="Diyarbakır",      Slug="diyarbakir",      PlateCode=21 },
        new() { Id=22, Name="Edirne",          Slug="edirne",          PlateCode=22 },
        new() { Id=23, Name="Elazığ",          Slug="elazig",          PlateCode=23 },
        new() { Id=24, Name="Erzincan",        Slug="erzincan",        PlateCode=24 },
        new() { Id=25, Name="Erzurum",         Slug="erzurum",         PlateCode=25 },
        new() { Id=26, Name="Eskişehir",       Slug="eskisehir",       PlateCode=26 },
        new() { Id=27, Name="Gaziantep",       Slug="gaziantep",       PlateCode=27 },
        new() { Id=28, Name="Giresun",         Slug="giresun",         PlateCode=28 },
        new() { Id=29, Name="Gümüşhane",       Slug="gumushane",       PlateCode=29 },
        new() { Id=30, Name="Hakkari",         Slug="hakkari",         PlateCode=30 },
        new() { Id=31, Name="Hatay",           Slug="hatay",           PlateCode=31 },
        new() { Id=32, Name="Isparta",         Slug="isparta",         PlateCode=32 },
        new() { Id=33, Name="Mersin",          Slug="mersin",          PlateCode=33 },
        new() { Id=34, Name="İstanbul",        Slug="istanbul",        PlateCode=34 },
        new() { Id=35, Name="İzmir",           Slug="izmir",           PlateCode=35 },
        new() { Id=36, Name="Kars",            Slug="kars",            PlateCode=36 },
        new() { Id=37, Name="Kastamonu",       Slug="kastamonu",       PlateCode=37 },
        new() { Id=38, Name="Kayseri",         Slug="kayseri",         PlateCode=38 },
        new() { Id=39, Name="Kırklareli",      Slug="kirklareli",      PlateCode=39 },
        new() { Id=40, Name="Kırşehir",        Slug="kirsehir",        PlateCode=40 },
        new() { Id=41, Name="Kocaeli",         Slug="kocaeli",         PlateCode=41 },
        new() { Id=42, Name="Konya",           Slug="konya",           PlateCode=42 },
        new() { Id=43, Name="Kütahya",         Slug="kutahya",         PlateCode=43 },
        new() { Id=44, Name="Malatya",         Slug="malatya",         PlateCode=44 },
        new() { Id=45, Name="Manisa",          Slug="manisa",          PlateCode=45 },
        new() { Id=46, Name="Kahramanmaraş",   Slug="kahramanmaras",   PlateCode=46 },
        new() { Id=47, Name="Mardin",          Slug="mardin",          PlateCode=47 },
        new() { Id=48, Name="Muğla",           Slug="mugla",           PlateCode=48 },
        new() { Id=49, Name="Muş",             Slug="mus",             PlateCode=49 },
        new() { Id=50, Name="Nevşehir",        Slug="nevsehir",        PlateCode=50 },
        new() { Id=51, Name="Niğde",           Slug="nigde",           PlateCode=51 },
        new() { Id=52, Name="Ordu",            Slug="ordu",            PlateCode=52 },
        new() { Id=53, Name="Rize",            Slug="rize",            PlateCode=53 },
        new() { Id=54, Name="Sakarya",         Slug="sakarya",         PlateCode=54 },
        new() { Id=55, Name="Samsun",          Slug="samsun",          PlateCode=55 },
        new() { Id=56, Name="Siirt",           Slug="siirt",           PlateCode=56 },
        new() { Id=57, Name="Sinop",           Slug="sinop",           PlateCode=57 },
        new() { Id=58, Name="Sivas",           Slug="sivas",           PlateCode=58 },
        new() { Id=59, Name="Tekirdağ",        Slug="tekirdag",        PlateCode=59 },
        new() { Id=60, Name="Tokat",           Slug="tokat",           PlateCode=60 },
        new() { Id=61, Name="Trabzon",         Slug="trabzon",         PlateCode=61 },
        new() { Id=62, Name="Tunceli",         Slug="tunceli",         PlateCode=62 },
        new() { Id=63, Name="Şanlıurfa",       Slug="sanliurfa",       PlateCode=63 },
        new() { Id=64, Name="Uşak",            Slug="usak",            PlateCode=64 },
        new() { Id=65, Name="Van",             Slug="van",             PlateCode=65 },
        new() { Id=66, Name="Yozgat",          Slug="yozgat",          PlateCode=66 },
        new() { Id=67, Name="Zonguldak",       Slug="zonguldak",       PlateCode=67 },
        new() { Id=68, Name="Aksaray",         Slug="aksaray",         PlateCode=68 },
        new() { Id=69, Name="Bayburt",         Slug="bayburt",         PlateCode=69 },
        new() { Id=70, Name="Karaman",         Slug="karaman",         PlateCode=70 },
        new() { Id=71, Name="Kırıkkale",       Slug="kirikkale",       PlateCode=71 },
        new() { Id=72, Name="Batman",          Slug="batman",          PlateCode=72 },
        new() { Id=73, Name="Şırnak",          Slug="sirnak",          PlateCode=73 },
        new() { Id=74, Name="Bartın",          Slug="bartin",          PlateCode=74 },
        new() { Id=75, Name="Ardahan",         Slug="ardahan",         PlateCode=75 },
        new() { Id=76, Name="Iğdır",           Slug="igdir",           PlateCode=76 },
        new() { Id=77, Name="Yalova",          Slug="yalova",          PlateCode=77 },
        new() { Id=78, Name="Karabük",         Slug="karabuk",         PlateCode=78 },
        new() { Id=79, Name="Kilis",           Slug="kilis",           PlateCode=79 },
        new() { Id=80, Name="Osmaniye",        Slug="osmaniye",        PlateCode=80 },
        new() { Id=81, Name="Düzce",           Slug="duzce",           PlateCode=81 },
    };

    // ════════════════════════════════════════════════════════
    // İLÇELER — Her ile ait tam liste
    // ════════════════════════════════════════════════════════
    private static List<District> GetDistricts(List<City> cities)
    {
        var dict = cities.ToDictionary(c => c.PlateCode, c => c.Id);
        var list = new List<District>();
        int id = 1;

        void Add(int plate, params string[] names)
        {
            foreach (var n in names)
                list.Add(new District { Id = id++, CityId = dict[plate], Name = n, Slug = Slugify(n) });
        }

        // 01 Adana
        Add(1, "Aladağ","Ceyhan","Çukurova","Feke","İmamoğlu","Karaisalı","Karataş","Kozan","Pozantı","Saimbeyli","Sarıçam","Seyhan","Tufanbeyli","Yumurtalık","Yüreğir");
        // 02 Adıyaman
        Add(2, "Besni","Çelikhan","Gerger","Gölbaşı","Kahta","Merkez","Samsat","Sincik","Tut");
        // 03 Afyonkarahisar
        Add(3, "Başmakçı","Bayat","Bolvadin","Çay","Çobanlar","Dazkırı","Dinar","Emirdağ","Evciler","Hocalar","İhsaniye","İscehisar","Kızılören","Merkez","Sandıklı","Sinanpaşa","Sultandağı","Şuhut");
        // 04 Ağrı
        Add(4, "Diyadin","Doğubayazıt","Eleşkirt","Hamur","Merkez","Patnos","Taşlıçay","Tutak");
        // 05 Amasya
        Add(5, "Göynücek","Gümüşhacıköy","Hamamözü","Merkez","Merzifon","Suluova","Taşova");
        // 06 Ankara
        Add(6, "Akyurt","Altındağ","Ayaş","Bala","Beypazarı","Çamlıdere","Çankaya","Çubuk","Elmadağ","Etimesgut","Evren","Gölbaşı","Güdül","Haymana","Kahramankazan","Kalecik","Keçiören","Kızılcahamam","Mamak","Nallıhan","Polatlı","Pursaklar","Sincan","Şereflikoçhisar","Yenimahalle");
        // 07 Antalya
        Add(7, "Akseki","Aksu","Alanya","Demre","Döşemealtı","Elmalı","Finike","Gazipaşa","Gündoğmuş","İbradı","Kaş","Kemer","Kepez","Konyaaltı","Korkuteli","Kumluca","Manavgat","Muratpaşa","Serik");
        // 08 Artvin
        Add(8, "Ardanuç","Arhavi","Borçka","Hopa","Kemalpaşa","Merkez","Murgul","Şavşat","Yusufeli");
        // 09 Aydın
        Add(9, "Bozdoğan","Buharkent","Çine","Didim","Efeler","Germencik","İncirliova","Karacasu","Karpuzlu","Koçarlı","Köşk","Kuşadası","Kuyucak","Nazilli","Söke","Sultanhisar","Yenipazar");
        // 10 Balıkesir
        Add(10, "Altıeylül","Ayvalık","Balya","Bandırma","Bigadiç","Burhaniye","Dursunbey","Edremit","Erdek","Gömeç","Gönen","Havran","İvrindi","Karesi","Kepsut","Manyas","Marmara","Savaştepe","Sındırgı","Susurluk");
        // 11 Bilecik
        Add(11, "Bozüyük","Gölpazarı","İnhisar","Merkez","Osmaneli","Pazaryeri","Söğüt","Yenipazar");
        // 12 Bingöl
        Add(12, "Adaklı","Genç","Karlıova","Kiğı","Merkez","Solhan","Yayladere","Yedisu");
        // 13 Bitlis
        Add(13, "Adilcevaz","Ahlat","Güroymak","Hizan","Merkez","Mutki","Tatvan");
        // 14 Bolu
        Add(14, "Dörtdivan","Gerede","Göynük","Kıbrıscık","Mengen","Merkez","Mudurnu","Seben","Yeniçağa");
        // 15 Burdur
        Add(15, "Ağlasun","Altınyayla","Bucak","Çavdır","Çeltikçi","Gölhisar","Karamanlı","Kemer","Merkez","Tefenni","Yeşilova");
        // 16 Bursa
        Add(16, "Büyükorhan","Gemlik","Gürsu","Harmancık","İnegöl","İznik","Karacabey","Keles","Kestel","Mudanya","Mustafakemalpaşa","Nilüfer","Orhaneli","Orhangazi","Osmangazi","Yenişehir","Yıldırım");
        // 17 Çanakkale
        Add(17, "Ayvacık","Bayramiç","Biga","Bozcaada","Çan","Eceabat","Ezine","Gelibolu","Gökçeada","Lapseki","Merkez","Yenice");
        // 18 Çankırı
        Add(18, "Atkaracalar","Bayramören","Çerkeş","Eldivan","Ilgaz","Kızılırmak","Korgun","Kurşunlu","Merkez","Orta","Şabanözü","Yapraklı");
        // 19 Çorum
        Add(19, "Alaca","Bayat","Boğazkale","Dodurga","İskilip","Kargı","Laçin","Mecitözü","Merkez","Oğuzlar","Ortaköy","Osmancık","Sungurlu","Uğurludağ");
        // 20 Denizli
        Add(20, "Acıpayam","Babadağ","Baklan","Bekilli","Beyağaç","Bozkurt","Buldan","Çal","Çameli","Çardak","Çivril","Güney","Honaz","Kale","Merkezefendi","Pamukkale","Sarayköy","Serinhisar","Tavas");
        // 21 Diyarbakır
        Add(21, "Bağlar","Bismil","Çermik","Çınar","Çüngüş","Dicle","Eğil","Ergani","Hani","Hazro","Kayapınar","Kocaköy","Kulp","Lice","Silvan","Sur","Yenişehir");
        // 22 Edirne
        Add(22, "Enez","Havsa","İpsala","Keşan","Lalapaşa","Meriç","Merkez","Süloğlu","Uzunköprü");
        // 23 Elazığ
        Add(23, "Ağın","Alacakaya","Arıcak","Baskil","Karakoçan","Keban","Kovancılar","Maden","Merkez","Palu","Sivrice");
        // 24 Erzincan
        Add(24, "Çayırlı","İliç","Kemah","Kemaliye","Merkez","Otlukbeli","Refahiye","Tercan","Üzümlü");
        // 25 Erzurum
        Add(25, "Aşkale","Aziziye","Çat","Hınıs","Horasan","İspir","Karaçoban","Karayazı","Köprüköy","Merkez","Narman","Oltu","Olur","Palandöken","Pasinler","Pazaryolu","Şenkaya","Tekman","Tortum","Uzundere","Yakutiye");
        // 26 Eskişehir
        Add(26, "Alpu","Beylikova","Çifteler","Günyüzü","Han","İnönü","Mahmudiye","Mihalgazi","Mihalıççık","Odunpazarı","Sarıcakaya","Seyitgazi","Sivrihisar","Tepebaşı");
        // 27 Gaziantep
        Add(27, "Araban","İslahiye","Karkamış","Nizip","Nurdağı","Oğuzeli","Şahinbey","Şehitkamil","Yavuzeli");
        // 28 Giresun
        Add(28, "Alucra","Bulancak","Çamoluk","Çanakçı","Dereli","Doğankent","Espiye","Eynesil","Görele","Güce","Keşap","Merkez","Piraziz","Şebinkarahisar","Tirebolu","Yağlıdere");
        // 29 Gümüşhane
        Add(29, "Kelkit","Köse","Kürtün","Merkez","Şiran","Torul");
        // 30 Hakkari
        Add(30, "Çukurca","Derecik","Merkez","Şemdinli","Yüksekova");
        // 31 Hatay
        Add(31, "Altınözü","Antakya","Arsuz","Belen","Defne","Dörtyol","Erzin","Hassa","İskenderun","Kırıkhan","Kumlu","Payas","Reyhanlı","Samandağ","Yayladağı");
        // 32 Isparta
        Add(32, "Aksu","Atabey","Eğirdir","Gelendost","Gönen","Keçiborlu","Merkez","Senirkent","Sütçüler","Şarkikaraağaç","Uluborlu","Yalvaç","Yenişarbademli");
        // 33 Mersin
        Add(33, "Akdeniz","Anamur","Aydıncık","Bozyazı","Çamlıyayla","Erdemli","Gülnar","Mezitli","Mut","Silifke","Tarsus","Toroslar","Yenişehir");
        // 34 İstanbul
        Add(34, "Adalar","Arnavutköy","Ataşehir","Avcılar","Bağcılar","Bahçelievler","Bakırköy","Başakşehir","Bayrampaşa","Beşiktaş","Beykoz","Beylikdüzü","Beyoğlu","Büyükçekmece","Çatalca","Çekmeköy","Esenler","Esenyurt","Eyüpsultan","Fatih","Gaziosmanpaşa","Güngören","Kadıköy","Kağıthane","Kartal","Küçükçekmece","Maltepe","Pendik","Sancaktepe","Sarıyer","Silivri","Sultanbeyli","Sultangazi","Şile","Şişli","Tuzla","Ümraniye","Üsküdar","Zeytinburnu");
        // 35 İzmir
        Add(35, "Aliağa","Balçova","Bayındır","Bayraklı","Bergama","Beydağ","Bornova","Buca","Çeşme","Çiğli","Dikili","Foça","Gaziemir","Güzelbahçe","Karabağlar","Karaburun","Karşıyaka","Kemalpaşa","Kınık","Kiraz","Konak","Menderes","Menemen","Narlıdere","Ödemiş","Seferihisar","Selçuk","Tire","Torbalı","Urla");
        // 36 Kars
        Add(36, "Akyaka","Arpaçay","Digor","Kağızman","Merkez","Sarıkamış","Selim","Susuz");
        // 37 Kastamonu
        Add(37, "Abana","Ağlı","Araç","Azdavay","Bozkurt","Cide","Çatalzeytin","Daday","Devrekani","Doğanyurt","Hanönü","İhsangazi","İnebolu","Küre","Merkez","Pınarbaşı","Seydiler","Şenpazar","Taşköprü","Tosya");
        // 38 Kayseri
        Add(38, "Akkışla","Bünyan","Develi","Felahiye","Hacılar","İncesu","Kocasinan","Melikgazi","Özvatan","Pınarbaşı","Sarıoğlan","Sarız","Talas","Tomarza","Yahyalı","Yeşilhisar");
        // 39 Kırklareli
        Add(39, "Babaeski","Demirköy","Kofçaz","Lüleburgaz","Merkez","Pehlivanköy","Pınarhisar","Vize");
        // 40 Kırşehir
        Add(40, "Akçakent","Akpınar","Boztepe","Çiçekdağı","Kaman","Merkez","Mucur");
        // 41 Kocaeli
        Add(41, "Başiskele","Çayırova","Darıca","Derince","Dilovası","Gebze","Gölcük","İzmit","Kandıra","Karamürsel","Kartepe","Körfez");
        // 42 Konya
        Add(42, "Ahırlı","Akören","Akşehir","Altınekin","Beyşehir","Bozkır","Cihanbeyli","Çeltik","Çumra","Derbent","Derebucak","Doğanhisar","Emirgazi","Ereğli","Güneysınır","Hadim","Halkapınar","Hüyük","Ilgın","Kadınhanı","Karapınar","Karatay","Kulu","Meram","Sarayönü","Selçuklu","Seydişehir","Taşkent","Tuzlukçu","Yalıhüyük","Yunak");
        // 43 Kütahya
        Add(43, "Altıntaş","Aslanapa","Çavdarhisar","Domaniç","Dumlupınar","Emet","Gediz","Hisarcık","Merkez","Pazarlar","Simav","Şaphane","Tavşanlı");
        // 44 Malatya
        Add(44, "Akçadağ","Arapgir","Arguvan","Battalgazi","Darende","Doğanşehir","Doğanyol","Hekimhan","Kale","Kuluncak","Pütürge","Yazıhan","Yeşilyurt");
        // 45 Manisa
        Add(45, "Ahmetli","Akhisar","Alaşehir","Demirci","Gölmarmara","Gördes","Kırkağaç","Köprübaşı","Kula","Merkez","Salihli","Sarıgöl","Saruhanlı","Selendi","Soma","Şehzadeler","Turgutlu","Yunusemre");
        // 46 Kahramanmaraş
        Add(46, "Afşin","Andırın","Çağlayancerit","Dulkadiroğlu","Ekinözü","Elbistan","Göksun","Nurhak","Onikişubat","Pazarcık","Türkoğlu");
        // 47 Mardin
        Add(47, "Artuklu","Dargeçit","Derik","Kızıltepe","Mazıdağı","Midyat","Nusaybin","Ömerli","Savur","Yeşilli");
        // 48 Muğla
        Add(48, "Bodrum","Dalaman","Datça","Fethiye","Kavaklıdere","Köyceğiz","Marmaris","Menteşe","Milas","Ortaca","Seydikemer","Ula","Yatağan");
        // 49 Muş
        Add(49, "Bulanık","Hasköy","Korkut","Malazgirt","Merkez","Varto");
        // 50 Nevşehir
        Add(50, "Acıgöl","Avanos","Derinkuyu","Gülşehir","Hacıbektaş","Kozaklı","Merkez","Ürgüp");
        // 51 Niğde
        Add(51, "Altunhisar","Bor","Çamardı","Çiftlik","Merkez","Ulukışla");
        // 52 Ordu
        Add(52, "Akkuş","Altınordu","Aybastı","Çamaş","Çatalpınar","Çaybaşı","Fatsa","Gölköy","Gülyalı","Gürgentepe","İkizce","Kabadüz","Kabataş","Korgan","Kumru","Mesudiye","Perşembe","Ulubey","Ünye");
        // 53 Rize
        Add(53, "Ardeşen","Çamlıhemşin","Çayeli","Derepazarı","Fındıklı","Güneysu","Hemşin","İkizdere","İyidere","Kalkandere","Merkez","Pazar");
        // 54 Sakarya
        Add(54, "Adapazarı","Akyazı","Arifiye","Erenler","Ferizli","Geyve","Hendek","Karapürçek","Karasu","Kaynarca","Kocaali","Mithatpaşa","Pamukova","Sapanca","Serdivan","Söğütlü","Taraklı");
        // 55 Samsun
        Add(55, "Alaçam","Asarcık","Atakum","Ayvacık","Bafra","Canik","Çarşamba","Havza","İlkadım","Kavak","Ladik","Ondokuzmayıs","Salıpazarı","Tekkeköy","Terme","Vezirköprü","Yakakent");
        // 56 Siirt
        Add(56, "Baykan","Eruh","Kurtalan","Merkez","Pervari","Şirvan","Tillo");
        // 57 Sinop
        Add(57, "Ayancık","Boyabat","Dikmen","Durağan","Erfelek","Gerze","Merkez","Saraydüzü","Türkeli");
        // 58 Sivas
        Add(58, "Akıncılar","Altınyayla","Divriği","Doğanşar","Gemerek","Gölova","Hafik","İmranlı","Kangal","Koyulhisar","Merkez","Suşehri","Şarkışla","Ulaş","Yıldızeli","Zara");
        // 59 Tekirdağ
        Add(59, "Çerkezköy","Çorlu","Ergene","Hayrabolu","Kapaklı","Malkara","Marmaraereğlisi","Muratlı","Saray","Süleymanpaşa","Şarköy");
        // 60 Tokat
        Add(60, "Almus","Artova","Başçiftlik","Erbaa","Merkez","Niksar","Pazar","Reşadiye","Sulusaray","Turhal","Yeşilyurt","Zile");
        // 61 Trabzon
        Add(61, "Akçaabat","Araklı","Arsin","Beşikdüzü","Çarşıbaşı","Çaykara","Dernekpazarı","Düzköy","Hayrat","Köprübaşı","Maçka","Of","Ortahisar","Sürmene","Şalpazarı","Tonya","Vakfıkebir","Yomra");
        // 62 Tunceli
        Add(62, "Çemişgezek","Hozat","Mazgirt","Merkez","Nazımiye","Ovacık","Pertek","Pülümür");
        // 63 Şanlıurfa
        Add(63, "Akçakale","Birecik","Bozova","Ceylanpınar","Eyyübiye","Halfeti","Haliliye","Harran","Hilvan","Karaköprü","Siverek","Suruç","Viranşehir");
        // 64 Uşak
        Add(64, "Banaz","Eşme","Karahallı","Merkez","Sivaslı","Ulubey");
        // 65 Van
        Add(65, "Bahçesaray","Başkale","Çaldıran","Çatak","Edremit","Erciş","Gevaş","Gürpınar","İpekyolu","Muradiye","Özalp","Saray","Tuşba");
        // 66 Yozgat
        Add(66, "Akdağmadeni","Aydıncık","Boğazlıyan","Çandır","Çayıralan","Çekerek","Kadışehri","Merkez","Saraykent","Sarıkaya","Şefaatli","Sorgun","Yenifakılı","Yerköy");
        // 67 Zonguldak
        Add(67, "Alaplı","Çaycuma","Devrek","Ereğli","Gökçebey","Kilimli","Kozlu","Merkez");
        // 68 Aksaray
        Add(68, "Ağaçören","Eskil","Gülağaç","Güzelyurt","Merkez","Ortaköy","Sarıyahşi","Sultanhanı");
        // 69 Bayburt
        Add(69, "Aydıntepe","Demirözü","Merkez");
        // 70 Karaman
        Add(70, "Ayrancı","Başyayla","Ermenek","Kazımkarabekir","Merkez","Sarıveliler");
        // 71 Kırıkkale
        Add(71, "Bahşili","Balışeyh","Çelebi","Delice","Karakeçili","Keskin","Merkez","Sulakyurt","Yahşihan");
        // 72 Batman
        Add(72, "Beşiri","Gercüş","Hasankeyf","Kozluk","Merkez","Sason");
        // 73 Şırnak
        Add(73, "Beytüşşebap","Cizre","Güçlükonak","İdil","Merkez","Silopi","Uludere");
        // 74 Bartın
        Add(74, "Amasra","Kurucaşile","Merkez","Ulus");
        // 75 Ardahan
        Add(75, "Çıldır","Damal","Göle","Hanak","Merkez","Posof");
        // 76 Iğdır
        Add(76, "Aralık","Karakoyunlu","Merkez","Tuzluca");
        // 77 Yalova
        Add(77, "Altınova","Armutlu","Çınarcık","Çiftlikköy","Merkez","Termal");
        // 78 Karabük
        Add(78, "Eflani","Eskipazar","Merkez","Ovacık","Safranbolu","Yenice");
        // 79 Kilis
        Add(79, "Elbeyli","Merkez","Musabeyli","Polateli");
        // 80 Osmaniye
        Add(80, "Bahçe","Düziçi","Hasanbeyli","Kadirli","Merkez","Sumbas","Toprakkale");
        // 81 Düzce
        Add(81, "Akçakoca","Cumayeri","Çilimli","Gölyaka","Gümüşova","Kaynaşlı","Merkez","Yığılca");

        return list;
    }

    private static Dictionary<int, string[]> GetDistrictsByPlate() => new()
    {
        [1]  = ["Aladağ","Ceyhan","Çukurova","Feke","İmamoğlu","Karaisalı","Karataş","Kozan","Pozantı","Saimbeyli","Sarıçam","Seyhan","Tufanbeyli","Yumurtalık","Yüreğir"],
        [2]  = ["Besni","Çelikhan","Gerger","Gölbaşı","Kahta","Merkez","Samsat","Sincik","Tut"],
        [3]  = ["Başmakçı","Bayat","Bolvadin","Çay","Çobanlar","Dazkırı","Dinar","Emirdağ","Evciler","Hocalar","İhsaniye","İscehisar","Kızılören","Merkez","Sandıklı","Sinanpaşa","Sultandağı","Şuhut"],
        [4]  = ["Diyadin","Doğubayazıt","Eleşkirt","Hamur","Merkez","Patnos","Taşlıçay","Tutak"],
        [5]  = ["Göynücek","Gümüşhacıköy","Hamamözü","Merkez","Merzifon","Suluova","Taşova"],
        [6]  = ["Akyurt","Altındağ","Ayaş","Bala","Beypazarı","Çamlıdere","Çankaya","Çubuk","Elmadağ","Etimesgut","Evren","Gölbaşı","Güdül","Haymana","Kahramankazan","Kalecik","Keçiören","Kızılcahamam","Mamak","Nallıhan","Polatlı","Pursaklar","Sincan","Şereflikoçhisar","Yenimahalle"],
        [7]  = ["Akseki","Aksu","Alanya","Demre","Döşemealtı","Elmalı","Finike","Gazipaşa","Gündoğmuş","İbradı","Kaş","Kemer","Kepez","Konyaaltı","Korkuteli","Kumluca","Manavgat","Muratpaşa","Serik"],
        [8]  = ["Ardanuç","Arhavi","Borçka","Hopa","Kemalpaşa","Merkez","Murgul","Şavşat","Yusufeli"],
        [9]  = ["Bozdoğan","Buharkent","Çine","Didim","Efeler","Germencik","İncirliova","Karacasu","Karpuzlu","Koçarlı","Köşk","Kuşadası","Kuyucak","Nazilli","Söke","Sultanhisar","Yenipazar"],
        [10] = ["Altıeylül","Ayvalık","Balya","Bandırma","Bigadiç","Burhaniye","Dursunbey","Edremit","Erdek","Gömeç","Gönen","Havran","İvrindi","Karesi","Kepsut","Manyas","Marmara","Savaştepe","Sındırgı","Susurluk"],
        [11] = ["Bozüyük","Gölpazarı","İnhisar","Merkez","Osmaneli","Pazaryeri","Söğüt","Yenipazar"],
        [12] = ["Adaklı","Genç","Karlıova","Kiğı","Merkez","Solhan","Yayladere","Yedisu"],
        [13] = ["Adilcevaz","Ahlat","Güroymak","Hizan","Merkez","Mutki","Tatvan"],
        [14] = ["Dörtdivan","Gerede","Göynük","Kıbrıscık","Mengen","Merkez","Mudurnu","Seben","Yeniçağa"],
        [15] = ["Ağlasun","Altınyayla","Bucak","Çavdır","Çeltikçi","Gölhisar","Karamanlı","Kemer","Merkez","Tefenni","Yeşilova"],
        [16] = ["Büyükorhan","Gemlik","Gürsu","Harmancık","İnegöl","İznik","Karacabey","Keles","Kestel","Mudanya","Mustafakemalpaşa","Nilüfer","Orhaneli","Orhangazi","Osmangazi","Yenişehir","Yıldırım"],
        [17] = ["Ayvacık","Bayramiç","Biga","Bozcaada","Çan","Eceabat","Ezine","Gelibolu","Gökçeada","Lapseki","Merkez","Yenice"],
        [18] = ["Atkaracalar","Bayramören","Çerkeş","Eldivan","Ilgaz","Kızılırmak","Korgun","Kurşunlu","Merkez","Orta","Şabanözü","Yapraklı"],
        [19] = ["Alaca","Bayat","Boğazkale","Dodurga","İskilip","Kargı","Laçin","Mecitözü","Merkez","Oğuzlar","Ortaköy","Osmancık","Sungurlu","Uğurludağ"],
        [20] = ["Acıpayam","Babadağ","Baklan","Bekilli","Beyağaç","Bozkurt","Buldan","Çal","Çameli","Çardak","Çivril","Güney","Honaz","Kale","Merkezefendi","Pamukkale","Sarayköy","Serinhisar","Tavas"],
        [21] = ["Bağlar","Bismil","Çermik","Çınar","Çüngüş","Dicle","Eğil","Ergani","Hani","Hazro","Kayapınar","Kocaköy","Kulp","Lice","Silvan","Sur","Yenişehir"],
        [22] = ["Enez","Havsa","İpsala","Keşan","Lalapaşa","Meriç","Merkez","Süloğlu","Uzunköprü"],
        [23] = ["Ağın","Alacakaya","Arıcak","Baskil","Karakoçan","Keban","Kovancılar","Maden","Merkez","Palu","Sivrice"],
        [24] = ["Çayırlı","İliç","Kemah","Kemaliye","Merkez","Otlukbeli","Refahiye","Tercan","Üzümlü"],
        [25] = ["Aşkale","Aziziye","Çat","Hınıs","Horasan","İspir","Karaçoban","Karayazı","Köprüköy","Merkez","Narman","Oltu","Olur","Palandöken","Pasinler","Pazaryolu","Şenkaya","Tekman","Tortum","Uzundere","Yakutiye"],
        [26] = ["Alpu","Beylikova","Çifteler","Günyüzü","Han","İnönü","Mahmudiye","Mihalgazi","Mihalıççık","Odunpazarı","Sarıcakaya","Seyitgazi","Sivrihisar","Tepebaşı"],
        [27] = ["Araban","İslahiye","Karkamış","Nizip","Nurdağı","Oğuzeli","Şahinbey","Şehitkamil","Yavuzeli"],
        [28] = ["Alucra","Bulancak","Çamoluk","Çanakçı","Dereli","Doğankent","Espiye","Eynesil","Görele","Güce","Keşap","Merkez","Piraziz","Şebinkarahisar","Tirebolu","Yağlıdere"],
        [29] = ["Kelkit","Köse","Kürtün","Merkez","Şiran","Torul"],
        [30] = ["Çukurca","Derecik","Merkez","Şemdinli","Yüksekova"],
        [31] = ["Altınözü","Antakya","Arsuz","Belen","Defne","Dörtyol","Erzin","Hassa","İskenderun","Kırıkhan","Kumlu","Payas","Reyhanlı","Samandağ","Yayladağı"],
        [32] = ["Aksu","Atabey","Eğirdir","Gelendost","Gönen","Keçiborlu","Merkez","Senirkent","Sütçüler","Şarkikaraağaç","Uluborlu","Yalvaç","Yenişarbademli"],
        [33] = ["Akdeniz","Anamur","Aydıncık","Bozyazı","Çamlıyayla","Erdemli","Gülnar","Mezitli","Mut","Silifke","Tarsus","Toroslar","Yenişehir"],
        [34] = ["Adalar","Arnavutköy","Ataşehir","Avcılar","Bağcılar","Bahçelievler","Bakırköy","Başakşehir","Bayrampaşa","Beşiktaş","Beykoz","Beylikdüzü","Beyoğlu","Büyükçekmece","Çatalca","Çekmeköy","Esenler","Esenyurt","Eyüpsultan","Fatih","Gaziosmanpaşa","Güngören","Kadıköy","Kağıthane","Kartal","Küçükçekmece","Maltepe","Pendik","Sancaktepe","Sarıyer","Silivri","Sultanbeyli","Sultangazi","Şile","Şişli","Tuzla","Ümraniye","Üsküdar","Zeytinburnu"],
        [35] = ["Aliağa","Balçova","Bayındır","Bayraklı","Bergama","Beydağ","Bornova","Buca","Çeşme","Çiğli","Dikili","Foça","Gaziemir","Güzelbahçe","Karabağlar","Karaburun","Karşıyaka","Kemalpaşa","Kınık","Kiraz","Konak","Menderes","Menemen","Narlıdere","Ödemiş","Seferihisar","Selçuk","Tire","Torbalı","Urla"],
        [36] = ["Akyaka","Arpaçay","Digor","Kağızman","Merkez","Sarıkamış","Selim","Susuz"],
        [37] = ["Abana","Ağlı","Araç","Azdavay","Bozkurt","Cide","Çatalzeytin","Daday","Devrekani","Doğanyurt","Hanönü","İhsangazi","İnebolu","Küre","Merkez","Pınarbaşı","Seydiler","Şenpazar","Taşköprü","Tosya"],
        [38] = ["Akkışla","Bünyan","Develi","Felahiye","Hacılar","İncesu","Kocasinan","Melikgazi","Özvatan","Pınarbaşı","Sarıoğlan","Sarız","Talas","Tomarza","Yahyalı","Yeşilhisar"],
        [39] = ["Babaeski","Demirköy","Kofçaz","Lüleburgaz","Merkez","Pehlivanköy","Pınarhisar","Vize"],
        [40] = ["Akçakent","Akpınar","Boztepe","Çiçekdağı","Kaman","Merkez","Mucur"],
        [41] = ["Başiskele","Çayırova","Darıca","Derince","Dilovası","Gebze","Gölcük","İzmit","Kandıra","Karamürsel","Kartepe","Körfez"],
        [42] = ["Ahırlı","Akören","Akşehir","Altınekin","Beyşehir","Bozkır","Cihanbeyli","Çeltik","Çumra","Derbent","Derebucak","Doğanhisar","Emirgazi","Ereğli","Güneysınır","Hadim","Halkapınar","Hüyük","Ilgın","Kadınhanı","Karapınar","Karatay","Kulu","Meram","Sarayönü","Selçuklu","Seydişehir","Taşkent","Tuzlukçu","Yalıhüyük","Yunak"],
        [43] = ["Altıntaş","Aslanapa","Çavdarhisar","Domaniç","Dumlupınar","Emet","Gediz","Hisarcık","Merkez","Pazarlar","Simav","Şaphane","Tavşanlı"],
        [44] = ["Akçadağ","Arapgir","Arguvan","Battalgazi","Darende","Doğanşehir","Doğanyol","Hekimhan","Kale","Kuluncak","Pütürge","Yazıhan","Yeşilyurt"],
        [45] = ["Ahmetli","Akhisar","Alaşehir","Demirci","Gölmarmara","Gördes","Kırkağaç","Köprübaşı","Kula","Merkez","Salihli","Sarıgöl","Saruhanlı","Selendi","Soma","Şehzadeler","Turgutlu","Yunusemre"],
        [46] = ["Afşin","Andırın","Çağlayancerit","Dulkadiroğlu","Ekinözü","Elbistan","Göksun","Nurhak","Onikişubat","Pazarcık","Türkoğlu"],
        [47] = ["Artuklu","Dargeçit","Derik","Kızıltepe","Mazıdağı","Midyat","Nusaybin","Ömerli","Savur","Yeşilli"],
        [48] = ["Bodrum","Dalaman","Datça","Fethiye","Kavaklıdere","Köyceğiz","Marmaris","Menteşe","Milas","Ortaca","Seydikemer","Ula","Yatağan"],
        [49] = ["Bulanık","Hasköy","Korkut","Malazgirt","Merkez","Varto"],
        [50] = ["Acıgöl","Avanos","Derinkuyu","Gülşehir","Hacıbektaş","Kozaklı","Merkez","Ürgüp"],
        [51] = ["Altunhisar","Bor","Çamardı","Çiftlik","Merkez","Ulukışla"],
        [52] = ["Akkuş","Altınordu","Aybastı","Çamaş","Çatalpınar","Çaybaşı","Fatsa","Gölköy","Gülyalı","Gürgentepe","İkizce","Kabadüz","Kabataş","Korgan","Kumru","Mesudiye","Perşembe","Ulubey","Ünye"],
        [53] = ["Ardeşen","Çamlıhemşin","Çayeli","Derepazarı","Fındıklı","Güneysu","Hemşin","İkizdere","İyidere","Kalkandere","Merkez","Pazar"],
        [54] = ["Adapazarı","Akyazı","Arifiye","Erenler","Ferizli","Geyve","Hendek","Karapürçek","Karasu","Kaynarca","Kocaali","Mithatpaşa","Pamukova","Sapanca","Serdivan","Söğütlü","Taraklı"],
        [55] = ["Alaçam","Asarcık","Atakum","Ayvacık","Bafra","Canik","Çarşamba","Havza","İlkadım","Kavak","Ladik","Ondokuzmayıs","Salıpazarı","Tekkeköy","Terme","Vezirköprü","Yakakent"],
        [56] = ["Baykan","Eruh","Kurtalan","Merkez","Pervari","Şirvan","Tillo"],
        [57] = ["Ayancık","Boyabat","Dikmen","Durağan","Erfelek","Gerze","Merkez","Saraydüzü","Türkeli"],
        [58] = ["Akıncılar","Altınyayla","Divriği","Doğanşar","Gemerek","Gölova","Hafik","İmranlı","Kangal","Koyulhisar","Merkez","Suşehri","Şarkışla","Ulaş","Yıldızeli","Zara"],
        [59] = ["Çerkezköy","Çorlu","Ergene","Hayrabolu","Kapaklı","Malkara","Marmaraereğlisi","Muratlı","Saray","Süleymanpaşa","Şarköy"],
        [60] = ["Almus","Artova","Başçiftlik","Erbaa","Merkez","Niksar","Pazar","Reşadiye","Sulusaray","Turhal","Yeşilyurt","Zile"],
        [61] = ["Akçaabat","Araklı","Arsin","Beşikdüzü","Çarşıbaşı","Çaykara","Dernekpazarı","Düzköy","Hayrat","Köprübaşı","Maçka","Of","Ortahisar","Sürmene","Şalpazarı","Tonya","Vakfıkebir","Yomra"],
        [62] = ["Çemişgezek","Hozat","Mazgirt","Merkez","Nazımiye","Ovacık","Pertek","Pülümür"],
        [63] = ["Akçakale","Birecik","Bozova","Ceylanpınar","Eyyübiye","Halfeti","Haliliye","Harran","Hilvan","Karaköprü","Siverek","Suruç","Viranşehir"],
        [64] = ["Banaz","Eşme","Karahallı","Merkez","Sivaslı","Ulubey"],
        [65] = ["Bahçesaray","Başkale","Çaldıran","Çatak","Edremit","Erciş","Gevaş","Gürpınar","İpekyolu","Muradiye","Özalp","Saray","Tuşba"],
        [66] = ["Akdağmadeni","Aydıncık","Boğazlıyan","Çandır","Çayıralan","Çekerek","Kadışehri","Merkez","Saraykent","Sarıkaya","Şefaatli","Sorgun","Yenifakılı","Yerköy"],
        [67] = ["Alaplı","Çaycuma","Devrek","Ereğli","Gökçebey","Kilimli","Kozlu","Merkez"],
        [68] = ["Ağaçören","Eskil","Gülağaç","Güzelyurt","Merkez","Ortaköy","Sarıyahşi","Sultanhanı"],
        [69] = ["Aydıntepe","Demirözü","Merkez"],
        [70] = ["Ayrancı","Başyayla","Ermenek","Kazımkarabekir","Merkez","Sarıveliler"],
        [71] = ["Bahşili","Balışeyh","Çelebi","Delice","Karakeçili","Keskin","Merkez","Sulakyurt","Yahşihan"],
        [72] = ["Beşiri","Gercüş","Hasankeyf","Kozluk","Merkez","Sason"],
        [73] = ["Beytüşşebap","Cizre","Güçlükonak","İdil","Merkez","Silopi","Uludere"],
        [74] = ["Amasra","Kurucaşile","Merkez","Ulus"],
        [75] = ["Çıldır","Damal","Göle","Hanak","Merkez","Posof"],
        [76] = ["Aralık","Karakoyunlu","Merkez","Tuzluca"],
        [77] = ["Altınova","Armutlu","Çınarcık","Çiftlikköy","Merkez","Termal"],
        [78] = ["Eflani","Eskipazar","Merkez","Ovacık","Safranbolu","Yenice"],
        [79] = ["Elbeyli","Merkez","Musabeyli","Polateli"],
        [80] = ["Bahçe","Düziçi","Hasanbeyli","Kadirli","Merkez","Sumbas","Toprakkale"],
        [81] = ["Akçakoca","Cumayeri","Çilimli","Gölyaka","Gümüşova","Kaynaşlı","Merkez","Yığılca"],
    };

    private static string Slugify(string name) =>
        name.ToLowerInvariant()
            .Replace("ş","s").Replace("ğ","g").Replace("ü","u")
            .Replace("ö","o").Replace("ı","i").Replace("ç","c")
            .Replace("İ","i").Replace("Ş","s").Replace("Ğ","g")
            .Replace(" ","-").Replace("'","");


    // ════════════════════════════════════════════════════════
    // BRANŞLAR — Kapsamlı liste, kategorili
    // ════════════════════════════════════════════════════════
    private static List<Branch> GetBranches()
    {
        int id = 1;
        int order = 1;
        var list = new List<Branch>();

        void Add(string name, string category, bool popular = false) =>
            list.Add(new Branch { Id = id++, Name = name, Slug = Slugify(name), Category = category, IsPopular = popular, DisplayOrder = order++ });

        // Akademik — Temel Dersler
        Add("Matematik",           "Akademik", popular: true);
        Add("Fizik",               "Akademik", popular: true);
        Add("Kimya",               "Akademik", popular: true);
        Add("Biyoloji",            "Akademik", popular: true);
        Add("Türkçe / Edebiyat",   "Akademik", popular: true);
        Add("Tarih",               "Akademik");
        Add("Coğrafya",            "Akademik");
        Add("Felsefe",             "Akademik");
        Add("Din Kültürü",         "Akademik");
        Add("İngilizce",           "Dil",      popular: true);
        Add("Almanca",             "Dil",      popular: true);
        Add("Fransızca",           "Dil");
        Add("İspanyolca",          "Dil");
        Add("İtalyanca",           "Dil");
        Add("Arapça",              "Dil");
        Add("Rusça",               "Dil");
        Add("Japonca",             "Dil");
        Add("Çince",               "Dil");
        Add("Korece",              "Dil");

        // Sınav Hazırlık
        Add("YKS / TYT Matematik", "Sınav Hazırlık", popular: true);
        Add("YKS / AYT Fizik",     "Sınav Hazırlık", popular: true);
        Add("YKS / AYT Kimya",     "Sınav Hazırlık", popular: true);
        Add("YKS / AYT Biyoloji",  "Sınav Hazırlık");
        Add("YKS / AYT Edebiyat",  "Sınav Hazırlık");
        Add("YKS / AYT Tarih",     "Sınav Hazırlık");
        Add("YKS / AYT Coğrafya",  "Sınav Hazırlık");
        Add("LGS Hazırlık",        "Sınav Hazırlık", popular: true);
        Add("KPSS",                "Sınav Hazırlık", popular: true);
        Add("ALES",                "Sınav Hazırlık");
        Add("YDS / YÖKDİL",        "Sınav Hazırlık");
        Add("DGS",                 "Sınav Hazırlık");
        Add("ÖSYM Sınavları",      "Sınav Hazırlık");

        // Teknoloji & Yazılım
        Add("Python",              "Yazılım", popular: true);
        Add("JavaScript",          "Yazılım", popular: true);
        Add("Java",                "Yazılım", popular: true);
        Add("C# / .NET",           "Yazılım", popular: true);
        Add("C / C++",             "Yazılım");
        Add("PHP",                 "Yazılım");
        Add("Swift / iOS",         "Yazılım");
        Add("Kotlin / Android",    "Yazılım");
        Add("React",               "Yazılım", popular: true);
        Add("Vue.js",              "Yazılım");
        Add("Angular",             "Yazılım");
        Add("Node.js",             "Yazılım");
        Add("SQL / Veritabanı",    "Yazılım");
        Add("Siber Güvenlik",      "Yazılım");
        Add("Veri Bilimi",         "Yazılım", popular: true);
        Add("Yapay Zeka / ML",     "Yazılım", popular: true);
        Add("Unity / Oyun Geliştirme", "Yazılım");
        Add("Web Tasarım",         "Yazılım");

        // Müzik
        Add("Piyano",              "Müzik", popular: true);
        Add("Gitar (Klasik)",      "Müzik", popular: true);
        Add("Gitar (Elektro/Akustik)", "Müzik");
        Add("Keman",               "Müzik");
        Add("Viyola",              "Müzik");
        Add("Çello",               "Müzik");
        Add("Flüt",                "Müzik");
        Add("Klarnet",             "Müzik");
        Add("Saksofon",            "Müzik");
        Add("Davul / Perküsyon",   "Müzik");
        Add("Bağlama / Saz",       "Müzik", popular: true);
        Add("Ud",                  "Müzik");
        Add("Şan / Vokal",         "Müzik");

        // Sanat & Tasarım
        Add("Resim",               "Sanat");
        Add("Yağlı Boya",          "Sanat");
        Add("Suluboya",            "Sanat");
        Add("Heykel",              "Sanat");
        Add("Grafik Tasarım",      "Sanat", popular: true);
        Add("Fotoğrafçılık",       "Sanat");
        Add("Video Düzenleme",     "Sanat");

        // Spor & Aktivite
        Add("Yüzme",               "Spor", popular: true);
        Add("Tenis",               "Spor");
        Add("Satranç",             "Spor", popular: true);
        Add("Yoga",                "Spor");
        Add("Pilates",             "Spor");
        Add("Dans (Salsa/Tango)",  "Spor");
        Add("Bale",                "Spor");
        Add("Jimnastik",           "Spor");
        Add("Futbol Antrenörlüğü", "Spor");
        Add("Basketbol",           "Spor");
        Add("Voleybol",            "Spor");
        Add("Dövüş Sanatları",     "Spor");

        // Diğer
        Add("Muhasebe",            "Diğer");
        Add("Girişimcilik",        "Diğer");
        Add("Diksiyon / Sunum",    "Diğer");
        Add("Hız Okuma",           "Diğer");
        Add("Zihin Haritası",      "Diğer");

        return list;
    }
}

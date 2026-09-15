namespace FH6ItalianRarities.Models;

public static class CarCatalog
{
    public const string ItalianActivityId = "italian";
    public const string BritishActivityId = "british";

    private static readonly TimeSpan ChinaStandardTimeOffset = TimeSpan.FromHours(8);

    public static IReadOnlyList<RareCarActivity> Activities { get; } =
    [
        new(
            ItalianActivityId,
            "意大利奇珍",
            "Italian Automotive",
            new DateTimeOffset(2026, 7, 16, 22, 30, 0, ChinaStandardTimeOffset),
            new DateTimeOffset(2026, 8, 13, 22, 30, 0, ChinaStandardTimeOffset),
            "意大利汽车经销店",
            3),
        new(
            BritishActivityId,
            "英国奇珍",
            "British Automotive",
            new DateTimeOffset(2026, 9, 10, 22, 30, 0, ChinaStandardTimeOffset),
            new DateTimeOffset(2026, 10, 8, 22, 30, 0, ChinaStandardTimeOffset),
            "英国汽车经销店",
            2)
    ];

    public static RareCarActivity DefaultActivity { get; } =
        Activities.MaxBy(activity => activity.FirstAvailableFrom)!;

    public static IReadOnlyList<CarOption> All { get; } =
    [
        // Slot 1
        Car(1, 1, 157, 1599, 2012, "法拉利", "Ferrari", "599XX Evolution", "599XX Evolution", .85m, true, true,
            "2,600,000 CR", "历代极速天花板，高组别测速区与竞速的 Meta 车型。", "599xx evo 599xxe 进化版 极速 神车"),
        Car(1, 2, 152, 2542, 2017, "阿尔法·罗密欧", "Alfa Romeo", "Giulia 四叶草版", "Giulia Quadrifoglio", .85m,
            aliases: "朱丽叶 giulia qv quadrifoglio 阿尔法罗密欧"),
        Car(1, 3, 175, 2038, 2014, "阿尔法·罗密欧", "Alfa Romeo", "4C", "4C", .75m,
            aliases: "阿尔法罗密欧"),
        Car(1, 4, 155, 3595, 2020, "法拉利", "Ferrari", "SF90 Stradale", "SF90 Stradale", .90m,
            aliases: "sf90 街道版"),
        Car(1, 5, 177, 3312, 2019, "法拉利", "Ferrari", "Monza SP2", "Monza SP2", .75m,
            aliases: "蒙扎 monza"),
        Car(1, 6, 178, 2974, 2017, "法拉利", "Ferrari", "812 Superfast", "812 Superfast", .75m,
            aliases: "812 超快"),
        Car(1, 7, 179, 2577, 2015, "法拉利", "Ferrari", "F12tdf", "F12tdf", .75m,
            aliases: "f12 tdf"),
        Car(1, 8, 180, 333, 2002, "法拉利", "Ferrari", "Enzo Ferrari", "Enzo Ferrari", .75m,
            aliases: "恩佐 enzo"),
        Car(1, 9, 183, 3753, 2022, "兰博基尼", "Lamborghini", "Huracán Tecnica", "Huracán Tecnica", .75m,
            aliases: "huracan tecnica 飓风 技术版 小牛"),
        Car(1, 10, 154, 3774, 2021, "兰博基尼", "Lamborghini", "Countach LPI 800-4", "Countach LPI 800-4", .90m,
            aliases: "countach 康塔什 lp800"),
        Car(1, 11, 184, 1173, 2010, "兰博基尼", "Lamborghini", "Murciélago LP 670-4 SV", "Murciélago LP 670-4 SV", .75m,
            aliases: "murcielago 蝙蝠 lp670 sv"),
        Car(1, 12, 185, 3082, 2008, "玛莎拉蒂", "Maserati", "MC12 Versione Corsa", "MC12 Versione Corsa", .75m,
            aliases: "mc12 corsa 赛道版"),
        Car(1, 13, 186, 2647, 2016, "帕加尼", "Pagani", "Huayra BC", "Huayra BC Coupé", .75m,
            aliases: "huayra 风神 bc coupe"),
        Car(1, 14, 159, 1398, 2012, "兰博基尼", "Lamborghini", "Aventador LP700-4", "Aventador LP700-4", .85m, true,
            referencePrice: "380,000 CR", description: "标志性 V12 四驱旗舰，抽中后的入手成本相对较低。", aliases: "aventador 埃文塔多 大牛 lp700"),

        // Slot 2
        Car(2, 1, 160, 1392, 2011, "兰博基尼", "Lamborghini", "Sesto Elemento", "Sesto Elemento", .85m, true, true,
            "2,500,000 CR", "著名的“轮椅级”神车，抓地力和过弯能力极强。", "第六元素 sesto 轮椅 神车"),
        Car(2, 2, 187, 3120, 2019, "兰博基尼", "Lamborghini", "Urus", "Urus", .75m,
            aliases: "urus 乌鲁斯 suv"),
        Car(2, 3, 188, 1601, 2012, "兰博基尼", "Lamborghini", "Gallardo LP570-4 Spyder Performante", "Gallardo LP570-4 Spyder Performante", .75m,
            aliases: "gallardo 盖拉多 spyder performante 小牛"),
        Car(2, 4, 189, 3672, 2020, "兰博基尼", "Lamborghini", "Huracán STO", "Huracán STO", .75m,
            aliases: "huracan 飓风 sto 小牛"),
        Car(2, 5, 190, 3611, 2021, "玛莎拉蒂", "Maserati", "MC20", "MC20", .75m),
        Car(2, 6, 191, 3543, 2022, "帕加尼", "Pagani", "Huayra R", "Huayra R", .75m,
            aliases: "huayra 风神 r"),
        Car(2, 7, 192, 1175, 2010, "帕加尼", "Pagani", "Zonda R", "Zonda R", .75m,
            aliases: "zonda 风之子 r"),
        Car(2, 8, 158, 3367, 2019, "法拉利", "Ferrari", "F8 Tributo", "F8 Tributo", .85m, true,
            referencePrice: "275,000 CR", description: "中置 V8 超跑，性能均衡且容易上手。", aliases: "f8 tributo 致敬"),
        Car(2, 9, 124, 1032, 2007, "阿尔法·罗密欧", "Alfa Romeo", "8C Competizione", "8C Competizione", .75m,
            aliases: "8c competizione 阿尔法罗密欧"),
        Car(2, 10, 194, 3227, 2019, "法拉利", "Ferrari", "488 Pista", "488 Pista", .75m,
            aliases: "488 pista 赛道版"),
        Car(2, 11, 195, 3226, 2017, "法拉利", "Ferrari", "J50", "J50", .75m),
        Car(2, 12, 196, 2034, 2013, "法拉利", "Ferrari", "LaFerrari", "LaFerrari", .75m,
            aliases: "拉法 laferrari"),
        Car(2, 13, 197, 1131, 2009, "法拉利", "Ferrari", "458 Italia", "458 Italia", .75m,
            aliases: "458 意大利"),
        Car(2, 14, 198, 1022, 2007, "法拉利", "Ferrari", "430 Scuderia", "430 Scuderia", .75m,
            aliases: "f430 430 scuderia"),

        // Slot 3
        Car(3, 1, 162, 1124, 1980, "菲亚特", "Fiat", "131 Abarth", "131 Abarth", .75m,
            aliases: "abarth 131 阿巴斯"),
        Car(3, 2, 163, 1150, 1965, "阿尔法·罗密欧", "Alfa Romeo", "Giulia Sprint GTA Stradale", "Giulia Sprint GTA Stradale", .75m,
            aliases: "giulia gta 阿尔法罗密欧"),
        Car(3, 3, 164, 1549, 1968, "阿尔法·罗密欧", "Alfa Romeo", "33 Stradale", "33 Stradale", .75m,
            aliases: "33 stradale 阿尔法罗密欧"),
        Car(3, 4, 165, 340, 1987, "法拉利", "Ferrari", "F40", "F40", .75m),
        Car(3, 5, 166, 1578, 1962, "法拉利", "Ferrari", "250 GT Berlinetta Lusso", "250 GT Berlinetta Lusso", .75m,
            aliases: "250gt berlinetta lusso"),
        Car(3, 6, 156, 358, 1984, "法拉利", "Ferrari", "288 GTO", "288 GTO", .85m, true,
            referencePrice: "3,500,000 CR", description: "经典法拉利旗舰，稀有度与收藏价值都很高。", aliases: "288gto"),
        Car(3, 7, 167, 326, 1969, "法拉利", "Ferrari", "Dino 246 GT", "Dino 246 GT", .75m,
            aliases: "dino 恐龙 246"),
        Car(3, 8, 168, 637, 1967, "兰博基尼", "Lamborghini", "Miura P400", "Miura P400", .75m,
            aliases: "miura 缪拉 p400"),
        Car(3, 9, 169, 1661, 1986, "蓝旗亚", "Lancia", "Delta S4", "Delta S4", .75m,
            aliases: "lancia delta s4 三角洲"),
        Car(3, 10, 170, 323, 1992, "蓝旗亚", "Lancia", "Delta HF Integrale EVO", "Delta HF Integrale EVO", .75m,
            aliases: "lancia delta hf integrale evolution 三角洲"),
        Car(3, 11, 171, 2017, 1968, "阿巴斯", "Abarth", "595 esseesse", "595 esseesse", .75m,
            aliases: "abarth 595 ss esseesse"),
        Car(3, 12, 172, 3062, 1970, "法拉利", "Ferrari", "512 S", "512 S", .75m),
        Car(3, 13, 138, 1023, 1989, "法拉利", "Ferrari", "F40 Competizione", "F40 Competizione", .75m,
            aliases: "f40c competizione 竞技版"),
        Car(3, 14, 161, 324, 1999, "兰博基尼", "Lamborghini", "Diablo GTR", "Diablo GTR", .85m, true,
            referencePrice: "1,000,000 CR", description: "轻量化赛道版 Diablo，兼具收藏价值与驾驶乐趣。", aliases: "diablo 迪亚波罗 gtr"),

        // British Automotive - Slot 2 / right booth (runtime pool A)
        BritishCar(2, 1, 199, 336, 1961, "捷豹", "Jaguar", "E-Type", "E-type", .75m, true,
            description: "经典英国老爷车，适当调校后可用于 B 级或 A 级复古跑车赛事。",
            aliases: "e type e-type 伊型 抽奖限定 wheelspin exclusive"),
        BritishCar(2, 2, 201, 1662, 1965, "MINI", "MINI", "Cooper S", "Cooper S", .75m,
            aliases: "mini 迷你 cooper s 65"),
        BritishCar(2, 3, 202, 1301, 1956, "捷豹", "Jaguar", "D-Type", "D-Type", .75m,
            aliases: "d type d-type"),
        BritishCar(2, 4, 204, 1314, 1993, "迈凯伦", "McLaren", "F1", "F1", .75m,
            aliases: "mclaren 麦克拉伦"),
        BritishCar(2, 5, 205, 1376, 1999, "莲花", "Lotus", "Elise Series 1", "Elise Series 1", .75m,
            aliases: "lotus 莲花 elise 99"),
        BritishCar(2, 6, 212, 3631, 2022, "阿斯顿·马丁", "Aston Martin", "Valkyrie AMR Pro", "Valkyrie AMR Pro", .75m,
            aliases: "aston martin 瓦尔基里 女武神 amr"),
        BritishCar(2, 7, 213, 338, 1997, "迈凯伦", "McLaren", "F1 GT", "F1 GT", .75m,
            aliases: "mclaren 麦克拉伦 longtail 长尾"),
        BritishCar(2, 8, 214, 3449, 2020, "莲花", "Lotus", "Evija", "Evija", .75m,
            aliases: "lotus 莲花 evija 纯电"),
        BritishCar(2, 9, 216, 3156, 2019, "迈凯伦", "McLaren", "Speedtail", "Speedtail", .75m,
            aliases: "mclaren 麦克拉伦 speed tail"),
        BritishCar(2, 10, 217, 433, 2005, "TVR", "TVR", "Sagaris", "Sagaris", .75m,
            aliases: "萨加里斯"),
        BritishCar(2, 11, 218, 2430, 2016, "阿里尔", "Ariel", "Nomad", "Nomad", .75m,
            aliases: "ariel 游牧者"),

        // British Automotive - Slot 1 / left booth (runtime pool B)
        BritishCar(1, 1, 200, 3185, 2019, "阿斯顿·马丁", "Aston Martin", "DBS Superleggera", "DBS Superleggera", .75m, true,
            description: "高性能英伦 GT，适合调校为 S1 级公路巡航。",
            aliases: "aston martin dbs superleggera 超轻版 西装暴徒 抽奖限定 wheelspin exclusive"),
        BritishCar(1, 2, 203, 2987, 1962, "皮尔", "Peel", "P50", "P50", .75m,
            aliases: "peel 皮尔 p 50"),
        BritishCar(1, 3, 206, 1253, 2010, "诺布尔", "Noble", "M600", "M600", .75m,
            aliases: "noble 诺贝尔"),
        BritishCar(1, 4, 207, 3728, 2021, "宾利", "Bentley", "Continental GT Convertible", "Continental GT Convertible", .75m,
            aliases: "bentley continental gtc 欧陆 敞篷"),
        BritishCar(1, 5, 208, 3599, 2022, "戈登·默里汽车", "Gordon Murray Automotive", "T.50", "T.50", .75m,
            aliases: "gma t50 gordon murray 戈登默里"),
        BritishCar(1, 6, 209, 3668, 2023, "迈凯伦", "McLaren", "Artura", "Artura", .75m,
            aliases: "mclaren 麦克拉伦 阿图拉"),
        BritishCar(1, 7, 210, 2569, 2015, "Ultima", "Ultima", "Evolution Coupe 1020", "Evolution Coupe 1020", .75m,
            aliases: "ultima evolution 1020 终极"),
        BritishCar(1, 8, 211, 3153, 2019, "迈凯伦", "McLaren", "600LT", "600LT", .75m,
            aliases: "mclaren 麦克拉伦 600 lt longtail 长尾"),
        BritishCar(1, 9, 215, 2494, 2015, "路虎", "Land Rover", "Range Rover Sport SVR", "Range Rover Sport SVR", .75m,
            aliases: "range rover land rover 路虎 揽胜 svr"),
        BritishCar(1, 10, 219, 1481, 1965, "奥斯汀-希利", "Austin-Healey", "3000 MKIII", "3000 MKIII", .75m,
            aliases: "austin healey 3000 mk3 奥斯汀希利"),
        BritishCar(1, 11, 220, 3293, 1993, "捷豹", "Jaguar", "XJ220S TWR", "XJ220S TWR", .75m,
            aliases: "xj220 s twr"),
    ];

    public static IReadOnlyList<CarOption> ForActivity(string activityId) =>
        All.Where(car => car.ActivityId == activityId)
            .OrderBy(car => car.Slot)
            .ThenBy(car => car.PoolPosition)
            .ToArray();

    public static IReadOnlyList<CarOption> ForSlot(string activityId, int slot) =>
        All.Where(car => car.ActivityId == activityId && car.Slot == slot)
            .OrderBy(car => car.PoolPosition)
            .ToArray();

    public static RareCarActivity GetActivity(string activityId) =>
        Activities.Single(activity => activity.Id == activityId);

    public static CarOption ByAftermarketId(int id) =>
        All.Single(car => car.AftermarketId == id);

    private static CarOption Car(
        int slot,
        int position,
        int aftermarketId,
        int carModelId,
        int year,
        string manufacturerZh,
        string manufacturerEn,
        string modelZh,
        string modelEn,
        decimal discount,
        bool limited = false,
        bool recommended = false,
        string? referencePrice = null,
        string? description = null,
        string aliases = "") =>
        new(ItalianActivityId, slot, position, aftermarketId, carModelId, year, manufacturerZh, manufacturerEn,
            modelZh, modelEn, discount, limited, recommended, referencePrice, description, aliases);

    private static CarOption BritishCar(
        int slot,
        int position,
        int aftermarketId,
        int carModelId,
        int year,
        string manufacturerZh,
        string manufacturerEn,
        string modelZh,
        string modelEn,
        decimal discount,
        bool limited = false,
        bool recommended = false,
        string? referencePrice = null,
        string? description = null,
        string aliases = "") =>
        new(BritishActivityId, slot, position, aftermarketId, carModelId, year, manufacturerZh, manufacturerEn,
            modelZh, modelEn, discount, limited, recommended, referencePrice, description, aliases);
}

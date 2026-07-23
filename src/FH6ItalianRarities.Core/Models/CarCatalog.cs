namespace FH6ItalianRarities.Models;

public static class CarCatalog
{
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
    ];

    public static IReadOnlyList<CarOption> ForSlot(int slot) =>
        All.Where(car => car.Slot == slot).OrderBy(car => car.PoolPosition).ToArray();

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
        new(slot, position, aftermarketId, carModelId, year, manufacturerZh, manufacturerEn,
            modelZh, modelEn, discount, limited, recommended, referencePrice, description, aliases);
}

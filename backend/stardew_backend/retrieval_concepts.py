"""Small bilingual domain vocabulary, not an embedding model or fact extractor.

Used only for retrieval; never for memory writing, negation or duplicate merging.
"""
import re
from functools import lru_cache

GROUPS = {
    "fishing": ("fishing", "angling", "angler", "钓鱼", "垂钓", "捕鱼"),
    "rain": ("rain", "rainy", "raining", "wet weather", "下雨", "雨天", "降雨"),
    "mining": ("mining", "mines", "ore", "矿洞", "矿井", "采矿", "矿石"),
    "coffee": ("coffee", "espresso", "咖啡", "浓缩咖啡"),
    "cooking": ("cooking", "cook", "cuisine", "烹饪", "做饭", "下厨"),
    "music": ("music", "band", "guitar", "drums", "音乐", "乐队", "吉他", "打鼓"),
    "photography": ("photography", "photograph", "camera", "darkroom", "摄影", "拍照", "相机", "暗房"),
    "pets": ("pet", "pets", "guinea pig", "宠物", "豚鼠", "仓鼠"),
    "home": ("home", "live", "residence", "住在哪里", "住在哪", "住在", "住处", "住宅", "帐篷"),
    "occupation": ("occupation", "profession", "job", "work", "programmer", "mayor", "manager", "doctor", "上班", "工作", "职业", "市长", "经理", "医生", "编程"),
    "family": ("family", "parents", "sister", "brother", "mother", "father", "家庭", "家人", "父母", "母亲", "父亲", "姐妹", "哥哥", "妹妹"),
    "gifts": ("gift", "gifts", "present", "presents", "送礼", "礼物"),
    "birthday": ("birthday", "生日", "诞辰"),
    "shopping": ("buy", "purchase", "sell", "shop", "出售", "购买", "买什么", "商店", "店铺"),
    "reading": ("reading", "books", "novel", "读书", "阅读", "小说"),
    "exercise": ("exercise", "training", "workout", "锻炼", "训练", "运动", "健身"),
    "pumpkin_spice": ("pumpkin spice", "南瓜香料"),
    "sleep": ("sleep", "bedtime", "stay up", "熬夜", "睡觉", "早睡"),
    "flowers": ("flowers", "flower", "鲜花", "花卉"),
    "money": ("money", "profit", "income", "赚钱", "收入", "利润"),
}


@lru_cache(maxsize=8192)
def concepts(text: str) -> frozenset[str]:
    text = re.sub(r"\s+", " ", text.lower())
    found = set()
    for name, terms in GROUPS.items():
        for term in terms:
            if (term in text if not term.isascii() else re.search(r"(?<![a-z])" + re.escape(term) + r"(?![a-z])", text)):
                found.add(name)
                break
    return frozenset(found)


def concept_score(query: frozenset[str], document: frozenset[str]) -> float:
    if not query or not document:
        return 0.0
    return len(query & document) / (len(query) * len(document)) ** 0.5

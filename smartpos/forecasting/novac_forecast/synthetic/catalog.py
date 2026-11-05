"""The FICTIONAL Novac chain: stores, suppliers, categories and products, with the parameters that shape demand.

Everything here is invented for development and testing. Store names use real neighbourhoods so the data reads
naturally, but no real business, brand or person is represented. Product names are generic (no brands).
"""

from dataclasses import dataclass, field
from datetime import date


@dataclass(frozen=True)
class Store:
    store_id: str
    store_name: str
    city: str
    region: str
    store_type: str          # Hypermarket / Supermarket / Neighbourhood
    latitude: float
    longitude: float
    state: str = "Tamil Nadu"


STORES: tuple[Store, ...] = (
    Store("STR001", "Novac T. Nagar", "Chennai", "Chennai Metro", "Hypermarket", 13.041800, 80.234100),
    Store("STR002", "Novac Velachery", "Chennai", "Chennai Metro", "Supermarket", 12.979100, 80.220900),
    Store("STR003", "Novac Anna Nagar", "Chennai", "Chennai Metro", "Supermarket", 13.085000, 80.210100),
    Store("STR004", "Novac R.S. Puram", "Coimbatore", "West (Kongu)", "Hypermarket", 11.008900, 76.950000),
    Store("STR005", "Novac Gandhipuram", "Coimbatore", "West (Kongu)", "Supermarket", 11.016800, 76.967500),
    Store("STR006", "Novac K.K. Nagar", "Madurai", "South", "Supermarket", 9.934700, 78.137300),
    Store("STR007", "Novac Thillai Nagar", "Trichy", "Central (Delta)", "Supermarket", 10.825700, 78.684900),
    Store("STR008", "Novac Fairlands", "Salem", "West (Kongu)", "Neighbourhood", 11.672400, 78.134900),
    Store("STR009", "Novac Perundurai Road", "Erode", "West (Kongu)", "Neighbourhood", 11.334600, 77.717200),
    Store("STR010", "Novac Palayamkottai", "Tirunelveli", "South", "Neighbourhood", 8.727100, 77.735600),
)

STORE_TYPE_SCALE = {"Hypermarket": 2.0, "Supermarket": 1.2, "Neighbourhood": 0.75}


@dataclass(frozen=True)
class Supplier:
    supplier_id: str
    supplier_name: str
    lead_days: int
    delay_probability: float   # chance an order arrives 1-4 days late


SUPPLIERS: tuple[Supplier, ...] = (
    Supplier("SUP001", "Kaveri Dairy Cooperative", 1, 0.03),
    Supplier("SUP002", "Koyambedu Fresh Produce Traders", 1, 0.10),
    Supplier("SUP003", "Thanjavur Rice & Grains", 4, 0.08),
    Supplier("SUP004", "Madurai Spice Merchants", 5, 0.10),
    Supplier("SUP005", "Southern Oils & Pulses", 6, 0.12),
    Supplier("SUP006", "Chennai Beverage Distributors", 3, 0.06),
    Supplier("SUP007", "Kongu Snacks & Sweets", 3, 0.08),
    Supplier("SUP008", "Coastal Confectionery Wholesale", 5, 0.10),
    Supplier("SUP009", "TN Personal Care Distributors", 8, 0.12),
    Supplier("SUP010", "Hygiene Home Supplies", 10, 0.15),
    Supplier("SUP011", "Pooja & Home Essentials", 6, 0.10),
    Supplier("SUP012", "Nilgiris Tea & Coffee Estates", 7, 0.10),
)

CATEGORIES: tuple[tuple[str, str], ...] = (
    ("CAT01", "Grocery"),
    ("CAT02", "Dairy"),
    ("CAT03", "Beverages"),
    ("CAT04", "Snacks"),
    ("CAT05", "Personal Care"),
    ("CAT06", "Household Products"),
    ("CAT07", "Fruits and Vegetables"),
)
CATEGORY_NAME = dict(CATEGORIES)

# Price sensitivity (elasticity of demand to price) by category: staples barely move, treats move a lot.
CATEGORY_ELASTICITY = {"CAT01": -0.6, "CAT02": -0.5, "CAT03": -1.4, "CAT04": -1.8, "CAT05": -1.1, "CAT06": -0.9, "CAT07": -1.2}

# Month-of-year demand multipliers by category (Jan..Dec). Summer (Apr-Jun) is hot; the north-east monsoon (Oct-Dec) is wet.
CATEGORY_SEASON = {
    "CAT01": (1.02, 0.98, 0.98, 0.99, 0.98, 0.98, 1.00, 1.00, 1.00, 1.02, 1.03, 1.02),
    "CAT02": (0.97, 0.98, 1.02, 1.08, 1.10, 1.06, 1.00, 0.98, 0.98, 0.98, 0.97, 0.98),
    "CAT03": (0.85, 0.92, 1.10, 1.35, 1.45, 1.30, 1.05, 1.00, 0.98, 0.92, 0.85, 0.88),
    "CAT04": (1.00, 0.97, 0.96, 0.95, 0.95, 0.97, 1.00, 1.00, 1.02, 1.08, 1.12, 1.06),
    "CAT05": (0.98, 0.98, 1.02, 1.06, 1.06, 1.02, 1.00, 0.99, 0.99, 1.00, 0.99, 1.00),
    "CAT06": (1.02, 0.98, 0.98, 0.98, 0.98, 0.98, 0.99, 1.00, 1.02, 1.08, 1.06, 1.00),
    "CAT07": (1.02, 1.00, 1.00, 1.02, 1.03, 1.00, 1.00, 0.99, 0.99, 0.97, 0.96, 0.98),
}

# How each region leans: city shoppers buy more drinks, snacks and personal care; smaller towns more staples and produce.
REGION_CATEGORY_AFFINITY = {
    "Chennai Metro": {"CAT01": 0.90, "CAT02": 1.00, "CAT03": 1.25, "CAT04": 1.15, "CAT05": 1.15, "CAT06": 1.05, "CAT07": 0.90},
    "West (Kongu)": {"CAT01": 1.10, "CAT02": 1.10, "CAT03": 0.90, "CAT04": 1.00, "CAT05": 0.95, "CAT06": 1.00, "CAT07": 1.05},
    "South": {"CAT01": 1.15, "CAT02": 0.95, "CAT03": 0.85, "CAT04": 0.95, "CAT05": 0.90, "CAT06": 0.95, "CAT07": 1.15},
    "Central (Delta)": {"CAT01": 1.15, "CAT02": 1.00, "CAT03": 0.90, "CAT04": 0.95, "CAT05": 0.95, "CAT06": 0.95, "CAT07": 1.10},
}

# Day-of-week multipliers (Mon..Sun): Chennai weekends are much busier than in smaller cities.
WEEKDAY_PROFILE = {
    "metro": (0.88, 0.86, 0.90, 0.94, 1.05, 1.32, 1.40),
    "town": (0.96, 0.95, 0.97, 0.98, 1.06, 1.16, 1.10),
}

# How strongly price matters by region (small towns are more price sensitive).
REGION_PRICE_SENSITIVITY = {"Chennai Metro": 0.85, "West (Kongu)": 1.05, "South": 1.20, "Central (Delta)": 1.15}
REGION_PRICE_INDEX = {"Chennai Metro": 1.03, "West (Kongu)": 1.00, "South": 0.99, "Central (Delta)": 0.99}


@dataclass(frozen=True)
class ProductSeed:
    name: str
    category_id: str
    price: float                 # base shelf price in INR at a reference store
    base_daily: float            # units/day at a reference supermarket with no special effects
    supplier_id: str
    moq: int                     # minimum order quantity (case size)
    perishable: bool = False
    festivals: dict[str, float] = field(default_factory=dict)   # peak multiplier around each festival
    summer: float = 1.0          # extra summer (Apr-Jun) multiplier on top of the category season
    launched: date | None = None  # products launched recently have no earlier history


def _p(name, cat, price, base, sup, moq, perishable=False, summer=1.0, launched=None, **festivals):
    return ProductSeed(name, cat, price, base, sup, moq, perishable, {k.replace("_", " "): v for k, v in festivals.items()}, summer, launched)


_DIWALI_SWEET = dict(Diwali=3.0)
PRODUCTS: tuple[ProductSeed, ...] = (
    # ---- Grocery (CAT01)
    _p("Ponni Raw Rice 5 kg", "CAT01", 345.00, 6.0, "SUP003", 10, Pongal=2.4),
    _p("Idli Rice 5 kg", "CAT01", 285.00, 4.5, "SUP003", 10),
    _p("Toor Dal 1 kg", "CAT01", 165.00, 7.0, "SUP005", 20, Diwali=1.3),
    _p("Moong Dal 500 g", "CAT01", 72.00, 4.0, "SUP005", 20, Pongal=2.6),
    _p("Urad Dal 1 kg", "CAT01", 148.00, 4.5, "SUP005", 20, Diwali=1.6),
    _p("Sunflower Oil 1 L", "CAT01", 152.00, 9.0, "SUP005", 24, Diwali=2.0),
    _p("Gingelly Oil 500 ml", "CAT01", 210.00, 3.0, "SUP005", 12, Diwali=2.2),
    _p("Wheat Atta 5 kg", "CAT01", 268.00, 3.5, "SUP003", 10),
    _p("Sugar 1 kg", "CAT01", 46.00, 11.0, "SUP003", 25, Diwali=1.8, Pongal=1.6),
    _p("Jaggery 1 kg", "CAT01", 92.00, 3.0, "SUP004", 20, Pongal=3.5, Vinayagar_Chaturthi=1.6, Tamil_New_Year=1.4),
    _p("Bombay Rava 1 kg", "CAT01", 58.00, 4.0, "SUP003", 20, Diwali=1.8),
    _p("Sambar Powder 200 g", "CAT01", 64.00, 5.0, "SUP004", 24),
    _p("Tamarind 500 g", "CAT01", 85.00, 3.0, "SUP004", 20),
    _p("Filter Coffee Powder 500 g", "CAT01", 245.00, 4.0, "SUP012", 12),
    _p("Ragi Malt Mix 500 g", "CAT01", 185.00, 2.5, "SUP012", 12, launched=date(2026, 8, 1)),
    # ---- Dairy (CAT02)
    _p("Toned Milk 500 ml", "CAT02", 28.00, 55.0, "SUP001", 24, perishable=True, Pongal=1.3, Diwali=1.4),
    _p("Full Cream Milk 1 L", "CAT02", 68.00, 14.0, "SUP001", 12, perishable=True, Diwali=1.5),
    _p("Curd 500 g", "CAT02", 42.00, 18.0, "SUP001", 12, perishable=True, summer=1.25),
    _p("Buttermilk 200 ml", "CAT02", 15.00, 16.0, "SUP001", 24, perishable=True, summer=1.6),
    _p("Ghee 500 ml", "CAT02", 330.00, 2.5, "SUP001", 6, Diwali=2.6, Pongal=2.0),
    _p("Butter 100 g", "CAT02", 58.00, 4.0, "SUP001", 12, perishable=True),
    _p("Paneer 200 g", "CAT02", 95.00, 3.5, "SUP001", 10, perishable=True, Diwali=1.6),
    _p("Cheese Slices 200 g", "CAT02", 145.00, 1.8, "SUP001", 10, perishable=True),
    _p("Badam Milk 180 ml", "CAT02", 35.00, 6.0, "SUP001", 24, perishable=True, summer=1.3),
    _p("Rose Milk 180 ml", "CAT02", 30.00, 5.0, "SUP001", 24, perishable=True, summer=1.4),
    _p("Flavoured Yogurt 100 g", "CAT02", 25.00, 6.0, "SUP001", 24, perishable=True),
    _p("Khoa 250 g", "CAT02", 120.00, 1.2, "SUP001", 10, perishable=True, Diwali=4.0),
    _p("Milk Powder 500 g", "CAT02", 260.00, 1.6, "SUP001", 12),
    _p("Condensed Milk 400 g", "CAT02", 135.00, 1.4, "SUP001", 12, Diwali=2.8, Christmas=1.8),
    _p("Dahi Cup 400 g", "CAT02", 48.00, 7.0, "SUP001", 12, perishable=True, summer=1.2),
    # ---- Beverages (CAT03)
    _p("Cola 750 ml", "CAT03", 40.00, 10.0, "SUP006", 24, summer=1.3, Christmas=1.5, New_Year=1.8),
    _p("Orange Soda 750 ml", "CAT03", 40.00, 6.0, "SUP006", 24, summer=1.3, New_Year=1.5),
    _p("Mango Drink 600 ml", "CAT03", 38.00, 9.0, "SUP006", 24, summer=1.5),
    _p("Mineral Water 1 L", "CAT03", 20.00, 24.0, "SUP006", 12, summer=1.6),
    _p("Mineral Water 5 L", "CAT03", 75.00, 4.0, "SUP006", 4, summer=1.4),
    _p("Tender Coconut Water 200 ml", "CAT03", 45.00, 4.0, "SUP006", 24, summer=1.8),
    _p("Lemon Soda 300 ml", "CAT03", 25.00, 6.0, "SUP006", 24, summer=1.5),
    _p("Energy Drink 250 ml", "CAT03", 110.00, 2.0, "SUP006", 24),
    _p("Fruit Juice 1 L", "CAT03", 120.00, 3.5, "SUP006", 12, summer=1.3, Diwali=1.4),
    _p("Tea Powder 250 g", "CAT03", 135.00, 6.0, "SUP012", 24),
    _p("Green Tea 25 bags", "CAT03", 160.00, 1.5, "SUP012", 12),
    _p("Instant Coffee 50 g", "CAT03", 175.00, 2.5, "SUP012", 24),
    _p("Health Drink Powder 500 g", "CAT03", 245.00, 2.0, "SUP012", 12),
    _p("Glucose Powder 500 g", "CAT03", 95.00, 1.8, "SUP006", 12, summer=1.6),
    _p("Cold Brew Coffee 200 ml", "CAT03", 95.00, 2.0, "SUP012", 24, launched=date(2026, 10, 10)),
    # ---- Snacks (CAT04)
    _p("Murukku 200 g", "CAT04", 60.00, 5.0, "SUP007", 24, Diwali=3.5, Pongal=1.4),
    _p("South Indian Mixture 400 g", "CAT04", 95.00, 4.5, "SUP007", 24, Diwali=3.2),
    _p("Banana Chips 200 g", "CAT04", 70.00, 5.0, "SUP007", 24, Diwali=2.0),
    _p("Potato Chips 90 g", "CAT04", 30.00, 9.0, "SUP008", 48, New_Year=1.6),
    _p("Glucose Biscuits 200 g", "CAT04", 25.00, 12.0, "SUP008", 48),
    _p("Cream Biscuits 150 g", "CAT04", 35.00, 7.0, "SUP008", 48),
    _p("Milk Chocolate 50 g", "CAT04", 50.00, 5.0, "SUP008", 48, Diwali=2.2, Christmas=1.8),
    _p("Mysore Pak 250 g", "CAT04", 140.00, 1.5, "SUP007", 12, **_DIWALI_SWEET),
    _p("Laddu 250 g", "CAT04", 120.00, 1.5, "SUP007", 12, Diwali=3.2, Vinayagar_Chaturthi=1.8),
    _p("Cashew Nuts 200 g", "CAT04", 210.00, 2.0, "SUP004", 12, Diwali=2.8, Pongal=2.4),
    _p("Raisins 200 g", "CAT04", 95.00, 1.5, "SUP004", 12, Pongal=2.0, Diwali=2.0),
    _p("Puffed Rice (Pori) 500 g", "CAT04", 40.00, 2.0, "SUP007", 24, Ayudha_Pooja=6.0),
    _p("Peanut Chikki 100 g", "CAT04", 30.00, 4.0, "SUP007", 48),
    _p("Rusk 300 g", "CAT04", 55.00, 3.5, "SUP008", 24),
    _p("Millet Muesli 500 g", "CAT04", 260.00, 1.5, "SUP008", 12, launched=date(2026, 9, 15)),
    # ---- Personal Care (CAT05)
    _p("Bath Soap 100 g", "CAT05", 38.00, 10.0, "SUP009", 48, Diwali=1.6),
    _p("Herbal Soap 125 g", "CAT05", 52.00, 4.0, "SUP009", 48, Diwali=1.5),
    _p("Shampoo 180 ml", "CAT05", 165.00, 3.0, "SUP009", 24),
    _p("Shampoo Sachet (pack of 10)", "CAT05", 20.00, 8.0, "SUP009", 50),
    _p("Toothpaste 150 g", "CAT05", 98.00, 5.0, "SUP009", 24),
    _p("Toothbrush", "CAT05", 35.00, 3.5, "SUP009", 24),
    _p("Coconut Hair Oil 200 ml", "CAT05", 105.00, 4.5, "SUP009", 24, Diwali=1.8),
    _p("Talcum Powder 100 g", "CAT05", 85.00, 2.5, "SUP009", 24, summer=1.5),
    _p("Face Wash 100 ml", "CAT05", 145.00, 2.0, "SUP009", 24),
    _p("Body Lotion 200 ml", "CAT05", 199.00, 1.5, "SUP009", 12),
    _p("Hand Wash 200 ml", "CAT05", 99.00, 2.5, "SUP009", 24),
    _p("Sanitary Pads (pack of 8)", "CAT05", 45.00, 4.0, "SUP009", 24),
    _p("Shaving Cream 70 g", "CAT05", 75.00, 1.5, "SUP009", 24),
    _p("Kajal Stick", "CAT05", 120.00, 1.0, "SUP009", 24, Diwali=1.8),
    _p("Sunscreen 50 ml", "CAT05", 245.00, 0.8, "SUP009", 12, summer=1.8),
    # ---- Household Products (CAT06)
    _p("Dishwash Bar 200 g", "CAT06", 25.00, 9.0, "SUP010", 48),
    _p("Dishwash Liquid 500 ml", "CAT06", 110.00, 3.0, "SUP010", 24),
    _p("Detergent Powder 1 kg", "CAT06", 135.00, 6.0, "SUP010", 20, Diwali=1.6),
    _p("Detergent Bar 250 g", "CAT06", 32.00, 6.0, "SUP010", 48),
    _p("Floor Cleaner 1 L", "CAT06", 189.00, 2.5, "SUP010", 12, Diwali=2.0, Pongal=1.6),
    _p("Toilet Cleaner 500 ml", "CAT06", 99.00, 2.5, "SUP010", 24, Diwali=1.6),
    _p("Agarbatti (pack of 100)", "CAT06", 60.00, 4.0, "SUP011", 48, Diwali=2.2, Ayudha_Pooja=2.0, Vinayagar_Chaturthi=1.8),
    _p("Camphor 50 g", "CAT06", 55.00, 3.0, "SUP011", 48, Diwali=2.0, Ayudha_Pooja=2.6, Pongal=1.6),
    _p("Matchbox (pack of 10)", "CAT06", 20.00, 5.0, "SUP011", 50, Diwali=2.5),
    _p("Mosquito Coil (pack of 10)", "CAT06", 45.00, 3.5, "SUP011", 48),
    _p("Garbage Bags (30)", "CAT06", 99.00, 2.0, "SUP010", 24, Diwali=1.5),
    _p("Coconut Broom", "CAT06", 85.00, 1.2, "SUP011", 12, Pongal=2.5),
    _p("Aluminium Foil 9 m", "CAT06", 120.00, 1.0, "SUP010", 12, Diwali=1.6),
    _p("Pooja Oil 1 L", "CAT06", 160.00, 1.5, "SUP011", 12, Diwali=2.8, Ayudha_Pooja=1.8),
    _p("Kolam Powder 1 kg", "CAT06", 40.00, 1.2, "SUP011", 24, Pongal=4.0, Diwali=2.0),
    # ---- Fruits and Vegetables (CAT07)
    _p("Onion 1 kg", "CAT07", 42.00, 22.0, "SUP002", 10, perishable=True),
    _p("Tomato 1 kg", "CAT07", 36.00, 20.0, "SUP002", 10, perishable=True),
    _p("Potato 1 kg", "CAT07", 38.00, 14.0, "SUP002", 10, perishable=True),
    _p("Banana (Robusta) 1 dozen", "CAT07", 60.00, 9.0, "SUP002", 6, perishable=True, Pongal=2.0, Ayudha_Pooja=1.8, Vinayagar_Chaturthi=1.6),
    _p("Coconut", "CAT07", 32.00, 12.0, "SUP002", 20, perishable=True, Pongal=1.8, Vinayagar_Chaturthi=1.8),
    _p("Curry Leaves Bunch", "CAT07", 10.00, 10.0, "SUP002", 20, perishable=True),
    _p("Green Chilli 250 g", "CAT07", 18.00, 9.0, "SUP002", 20, perishable=True),
    _p("Drumstick 250 g", "CAT07", 30.00, 5.0, "SUP002", 20, perishable=True),
    _p("Lemon (pack of 6)", "CAT07", 30.00, 6.0, "SUP002", 20, perishable=True, summer=1.6),
    _p("Apple 1 kg", "CAT07", 180.00, 3.0, "SUP002", 10, perishable=True, Diwali=1.6),
    _p("Carrot 500 g", "CAT07", 32.00, 7.0, "SUP002", 20, perishable=True),
    _p("Brinjal 500 g", "CAT07", 25.00, 6.0, "SUP002", 20, perishable=True),
    _p("Sugarcane (2 pieces)", "CAT07", 60.00, 0.3, "SUP002", 10, perishable=True, Pongal=30.0),
    _p("Fresh Turmeric Plant", "CAT07", 35.00, 0.2, "SUP002", 10, perishable=True, Pongal=30.0),
    _p("Watermelon (1 piece)", "CAT07", 90.00, 2.0, "SUP002", 6, perishable=True, summer=2.5),
)


def product_id(index: int) -> str:
    return f"PRD{index + 1:04d}"

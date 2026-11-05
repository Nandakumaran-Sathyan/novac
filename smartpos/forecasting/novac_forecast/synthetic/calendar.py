"""Tamil Nadu retail calendar used by both the synthetic data generator and the model's features.

Festival dates are the commonly published ones for Tamil Nadu. They drive FABRICATED demand here, so small
differences from an official holiday list do not matter; verify them before using real data.
Each festival has a "build-up" window: shoppers buy in the days before the day itself (Diwali sweets, oil and
crackers-season snacks; Pongal rice, jaggery, sugarcane and turmeric).
"""

from dataclasses import dataclass
from datetime import date, timedelta


@dataclass(frozen=True)
class Festival:
    name: str
    day: date
    build_up_days: int   # days before the festival with extra demand
    after_days: int      # days after the festival that still count (e.g. Kaanum Pongal)


FESTIVALS: tuple[Festival, ...] = (
    # 2024-2025
    Festival("Diwali", date(2024, 10, 31), 6, 0),
    Festival("Christmas", date(2024, 12, 25), 3, 0),
    Festival("New Year", date(2025, 1, 1), 1, 0),
    Festival("Pongal", date(2025, 1, 14), 4, 2),        # Bhogi 13th, Thai Pongal 14th, Mattu/Kaanum 15-16th
    Festival("Tamil New Year", date(2025, 4, 14), 2, 0),
    Festival("Vinayagar Chaturthi", date(2025, 8, 27), 2, 0),
    Festival("Ayudha Pooja", date(2025, 10, 1), 2, 1),
    Festival("Diwali", date(2025, 10, 20), 6, 0),
    Festival("Christmas", date(2025, 12, 25), 3, 0),
    # 2026
    Festival("New Year", date(2026, 1, 1), 1, 0),
    Festival("Pongal", date(2026, 1, 15), 4, 2),
    Festival("Tamil New Year", date(2026, 4, 14), 2, 0),
    Festival("Vinayagar Chaturthi", date(2026, 9, 14), 2, 0),
    Festival("Ayudha Pooja", date(2026, 10, 19), 2, 1),
    Festival("Diwali", date(2026, 11, 8), 6, 0),
    Festival("Christmas", date(2026, 12, 25), 3, 0),
)

FESTIVAL_NAMES: tuple[str, ...] = tuple(sorted({f.name for f in FESTIVALS}))


def festival_on(day: date) -> tuple[str | None, int | None]:
    """The festival whose window contains `day`, and the days until the festival itself (negative = after it)."""
    for f in FESTIVALS:
        start = f.day - timedelta(days=f.build_up_days)
        end = f.day + timedelta(days=f.after_days)
        if start <= day <= end:
            return f.name, (f.day - day).days
    return None, None


def days_to_next_festival(day: date, cap: int = 30) -> int:
    """Days until the next festival day (0 on the day), capped so far-away festivals all look the same."""
    upcoming = [(f.day - day).days for f in FESTIVALS if f.day >= day]
    return min(min(upcoming), cap) if upcoming else cap

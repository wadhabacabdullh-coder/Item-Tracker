"""Map 1: Villa Moretti - a luxury mansion at night.

Estate (cols 0-61) with gardens, pool, gatehouse and the villa itself.
A separate cellar/vault level (cols 64-87) is linked by two staircases:
  * the kitchen pantry stairs (public route)
  * a hidden stair behind the study bookshelf (secret route that bypasses the red vault door)
"""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from canvas import Canvas

c = Canvas(88, 60)
R = Canvas.route

c.meta.update(id='mansion', name='Villa Moretti', theme='mansion', ambient='0.26',
              description='A hillside villa hosting a private party. Gardens, a ballroom and a cellar full of secrets.')

# ---------------------------------------------------------------- estate & garden
c.room(0, 0, 61, 59, floor=',')
# Gate alcove in the south wall + driveway
c.fill(27, 59, 33, 59, '"')
c.fill(28, 35, 32, 58, '"')
# Fountain plaza
c.fill(24, 41, 36, 48, '"')
c.fill(27, 43, 33, 46, '~')
c.put(30, 44, 'i'); c.put(30, 45, 'i')
# Terrace path around the house front
c.fill(9, 35, 52, 36, '"')
# Parked cars on the forecourt
c.block(18, 38, ["ggg", "ggg"]); c.block(40, 38, ["ggg", "ggg"])
# Hedge rows and trees (bushes are walkable concealment)
for y in (40, 50):
    c.fill(3, y, 22, y, 'B'); c.fill(38, y, 58, y, 'B')
c.fill(3, 41, 3, 49, 'B')
for (x, y) in [(6, 44), (12, 46), (17, 43), (45, 44), (51, 46), (56, 42), (8, 54), (20, 55), (46, 56), (15, 52)]:
    c.put(x, y, 'T')
c.fill(1, 52, 6, 52, 'B'); c.fill(7, 53, 7, 58, 'B')
# North & side garden strips
for x in range(3, 60, 5):
    c.put(x, 2, 'B')
c.fill(2, 8, 2, 30, 'B')
for y in range(6, 32, 6):
    c.put(5, y, 'T')
# Pool area east
c.fill(55, 12, 59, 29, '"')
c.fill(56, 14, 58, 27, '~')
c.put(55, 11, 'p'); c.put(59, 11, 'p'); c.put(55, 30, 'p'); c.put(59, 30, 'p')
c.block(55, 31, ["c c"])

# Garden shed (west) with the fuse box for the garden lights
c.room(52, 52, 59, 58, floor='.')
c.put(52, 55, 'D')
c.block(57, 53, ["xx", "`x"])
c.put(53, 57, 'f')

# Gatehouse (south-east of the gate)
c.room(36, 52, 43, 58, floor='.')
c.put(36, 55, 'D')
c.block(38, 53, ["kk", "`c"])
c.put(42, 53, 'f'); c.put(42, 57, 'C')
c.put(40, 52, 'W')

# ---------------------------------------------------------------- the villa
c.room(8, 4, 53, 34, floor='.')
c.hline(8, 53, 13)
c.hline(8, 38, 22)
c.vline(21, 4, 34)
c.vline(38, 4, 34)
c.vline(25, 4, 13)

# Study (NW) - wood floor, bookshelves, a secret room behind the west shelves
c.fill(13, 5, 20, 12, ':')
c.vline(12, 5, 12, 'h')
c.put(12, 8, 'S')                     # secret bookshelf door
c.fill(9, 5, 11, 12, '_')             # hidden room (stone)
c.block(9, 5, ["==", "``"])            # hidden stairs down
c.put(11, 11, 'x')
c.block(15, 7, ["kkk", "`c`"])
c.block(18, 11, ["ss"])
c.put(20, 5, 'p'); c.put(13, 12, 'p')
c.put(21, 8, 'D')                     # study -> north corridor
c.put(14, 4, 'W'); c.put(18, 4, 'W')

# North corridor between study & master bedroom
c.fill(22, 13, 24, 13, '.')           # open into the gallery
c.fill(22, 5, 24, 12, ';')
c.put(23, 5, 'p')

# Master bedroom (target's quarters) with en-suite
c.fill(26, 5, 37, 12, ';')
c.put(25, 9, 'D')
c.vline(32, 5, 9); c.hline(32, 37, 9)
c.fill(33, 5, 37, 8, '_')
c.put(34, 9, 'D')
c.block(33, 5, ["yy`on"])
c.block(27, 5, ["bbb", "bbb"])
c.put(26, 5, 'f'); c.put(30, 5, 'f')
c.block(27, 10, ["C``s"])
c.put(37, 11, 'C')
c.put(29, 4, 'W'); c.put(35, 4, 'W')

# Guest suite (NE) with bathroom, window to the north garden
c.fill(39, 5, 52, 12, ':')
c.vline(47, 5, 9); c.hline(47, 53, 9)
c.fill(48, 5, 52, 8, '_')
c.put(50, 9, 'D')
c.block(48, 5, ["on`yy"])
c.block(40, 5, ["bb", "bb"])
c.put(43, 5, 'f'); c.put(40, 11, 'C'); c.block(44, 11, ["kc"])
c.put(45, 13, 'D')                    # guest suite -> ballroom
c.put(42, 4, 'W'); c.put(53, 11, 'W')

# Kitchen (W middle) with pantry + stairs to the cellar, servants' door outside
c.fill(9, 14, 20, 21, '_')
c.vline(13, 14, 16); c.hline(9, 13, 17)
c.put(13, 15, 'D')
c.block(9, 14, ["==", "``", "`x"])
c.block(15, 14, ["uuuuuu"])
c.block(15, 17, ["uuu", "uuu"])
c.put(20, 17, 'f'); c.put(20, 18, 'f')
c.put(9, 21, 'e')                     # gas canister
c.put(21, 17, 'D')                    # kitchen -> gallery
c.put(8, 19, 'D')                     # servants' door -> west garden
c.put(15, 22, 'D')                    # kitchen -> dining

# Gallery (centre) - pillars, sofas, art
c.fill(22, 14, 37, 21, '.')
for (x, y) in [(24, 15), (35, 15), (24, 20), (35, 20)]:
    c.put(x, y, 'i')
c.block(28, 16, ["ssss"]); c.block(28, 19, ["tttt"])
c.put(22, 14, 'p'); c.put(37, 14, 'p'); c.put(22, 21, 'p')
c.put(30, 13, 'D')                    # gallery -> master bedroom (north)
c.fill(29, 22, 30, 22, '.')           # archway gallery -> foyer
c.put(38, 17, 'D')                    # gallery -> ballroom

# Dining (SW)
c.fill(9, 23, 20, 33, ':')
c.block(11, 26, ["ccccccc", "ttttttt", "ttttttt", "ccccccc"])
c.put(9, 23, 'f'); c.put(20, 23, 'p'); c.put(9, 33, 'p'); c.put(20, 33, 'p')
c.put(8, 26, 'W'); c.put(8, 30, 'W')
c.put(21, 31, 'D')                    # dining -> guest bathroom corridor side
c.put(14, 34, 'W'); c.put(18, 34, 'W')

# Security room + guest WC carved from the foyer's west side
c.vline(28, 22, 34)
c.hline(21, 28, 29)
c.fill(22, 23, 27, 28, '.')
c.block(22, 23, ["qqqq", "`cc`"])
c.put(27, 23, 'f'); c.put(22, 28, 'C')
c.put(28, 26, 'D')                    # security -> foyer
c.fill(22, 30, 27, 33, '_')
c.block(22, 30, ["o`o"]); c.block(22, 33, ["nn"])
c.put(28, 31, 'D')                    # WC -> foyer

# Foyer (S centre)
c.fill(29, 23, 37, 33, ';')
c.put(29, 23, 'p'); c.put(37, 23, 'p')
c.block(31, 27, ["ttt"])
c.fill(30, 34, 31, 34, 'D')           # front doors
c.put(38, 28, 'D')                    # foyer -> ballroom
c.put(34, 34, 'W')

# Ballroom (E, tall) - piano, bar, pillars, tables
c.fill(39, 14, 52, 33, ':')
c.block(41, 15, ["PPP", "PPP"])
c.block(49, 15, ["uuuu"])
c.put(52, 16, 'j')
for (x, y) in [(41, 21), (41, 28), (50, 21), (50, 28)]:
    c.put(x, y, 'i')
c.block(44, 23, ["tt", "tt"]); c.block(47, 26, ["tt", "tt"]); c.block(44, 30, ["tt"])
c.put(53, 20, 'D'); c.put(53, 27, 'D')   # terrace doors -> pool
c.put(53, 16, 'W'); c.put(53, 24, 'W'); c.put(53, 31, 'W')
c.put(45, 34, 'W'); c.put(49, 34, 'W')

# ---------------------------------------------------------------- cellar & vault (separate level)
c.room(64, 2, 87, 24, floor='_')
c.block(65, 3, ["=="])                 # stairs up to the kitchen pantry
c.vline(71, 2, 24); c.hline(64, 71, 12)
c.put(71, 6, 'D'); c.put(68, 12, 'D')
# Wine cellar (east part, long aisles of racks)
c.fill(72, 3, 86, 12, ':')
for x in (74, 77, 80, 83):
    c.vline(x, 4, 10, 'h')
c.block(85, 3, ["xx"])
c.put(86, 12, 'e')
# Storage (SW part)
c.fill(65, 13, 70, 23, '.')
c.block(65, 14, ["xx`x", "x```", "````", "`xx`"])
c.put(70, 23, 'f'); c.put(65, 23, 'C')
c.put(71, 18, 'D')
# Tasting room / corridor (middle)
c.hline(72, 87, 13)
c.put(75, 13, 'D')
c.fill(72, 14, 77, 23, ';')
c.block(73, 17, ["ttt", "ttt"]); c.block(73, 16, ["ccc"]); c.block(73, 19, ["ccc"])
c.vline(78, 13, 24)
c.put(78, 16, 'M')                    # red-keycard vault door
# Vault
c.fill(79, 14, 86, 23, '.')
c.block(80, 15, ["xx``xx"])
c.put(86, 14, 'f'); c.put(79, 14, 'f')
c.block(84, 22, ["=="])               # hidden stairs from the study land inside the vault antechamber
c.vline(82, 19, 23, 'w'); c.put(82, 21, '.')

# ---------------------------------------------------------------- entities
c.ent('spawn at=3,56')
c.ent('extract rect=1,53,6,6 label="Getaway car"')
c.ent('stairs a=65,3 b=10,15')          # pantry <-> cellar
c.ent('stairs a=9,5 b=84,22 secret=1')  # hidden study stairs <-> vault

# Room labels (shown on the HUD)
for name, rect in [("Garden", "1,35,60,24"), ("Garden", "1,1,60,3"), ("Garden", "1,4,6,30"), ("Pool Terrace", "54,4,7,31"),
                   ("Garden Shed", "53,53,6,5"), ("Gatehouse", "37,53,6,5"), ("Study", "13,5,8,8"), ("Hidden Room", "9,5,3,8"),
                   ("North Corridor", "22,5,3,8"), ("Master Bedroom", "26,5,12,8"), ("Guest Suite", "39,5,14,8"),
                   ("Kitchen", "9,14,12,8"), ("Pantry", "9,14,4,3"), ("Gallery", "22,14,16,8"), ("Dining Room", "9,23,12,11"),
                   ("Security Room", "22,23,6,6"), ("Washroom", "22,30,6,4"), ("Foyer", "29,23,9,11"), ("Ballroom", "39,14,14,20"),
                   ("Cellar Stairs", "65,3,6,9"), ("Wine Cellar", "72,3,15,10"), ("Storage", "65,13,6,11"),
                   ("Tasting Room", "72,14,6,10"), ("Vault", "79,14,8,10")]:
    c.ent(f'zone name="{name}" rect={rect}')

# Lights: chandeliers & lamps (warm), garden lamps (group 'garden' on the shed fuse box), pool (cyan)
lights = [
    (30, 28, 7, 1.0, 'ffd9a0', None), (45, 18, 7, 1.0, 'ffd9a0', None), (45, 29, 7, 1.0, 'ffcf8a', None),
    (29, 17, 7, 0.9, 'ffe0b0', None), (16, 9, 5, 0.8, 'ffc070', None), (31, 9, 5, 0.6, 'ffb0a0', None),
    (15, 18, 6, 0.9, 'e8f0ff', None), (15, 28, 6, 0.9, 'ffd9a0', None), (25, 25, 4, 0.7, '80c0ff', None),
    (45, 8, 5, 0.6, 'ffd0a0', None), (24, 31, 3, 0.6, 'e8f0ff', None),
    (30, 44, 7, 0.8, 'fff0d0', 'garden'), (13, 45, 5, 0.6, 'fff0d0', 'garden'), (48, 45, 5, 0.6, 'fff0d0', 'garden'),
    (30, 56, 5, 0.8, 'fff0d0', 'garden'), (57, 20, 6, 0.9, '60e0ff', 'garden'), (4, 20, 4, 0.5, 'fff0d0', 'garden'),
    (39, 55, 4, 0.8, 'ffe0a0', None), (55, 55, 3, 0.5, 'ffa050', None),
    (67, 7, 4, 0.7, 'ffb060', None), (79, 8, 5, 0.6, 'ffb060', None), (75, 18, 4, 0.8, 'ffc080', None),
    (82, 18, 5, 0.9, 'ffe0a0', None), (67, 18, 4, 0.5, 'ffb060', True),
]
for (x, y, r, i, col, grp) in lights:
    extra = ''
    if grp is True:
        extra = ' flicker=1'
    elif grp:
        extra = f' group={grp}'
    c.ent(f'light at={x},{y} r={r} i={i} color={col}{extra}')

c.ent('powerbox at=53,57 group=garden')
c.ent('distraction at=41,15 kind=piano')
c.ent('distraction at=16,14 kind=radio')

# Guards
c.ent('guard id=gate at=31,57 facing=90 look="90,45,135"')
c.ent(f'guard id=drive at=30,52 route={R((30,52,2,270),(30,37,2,90),(24,37,0),(24,49,1))}')
c.ent(f'guard id=westg at=4,48 route={R((4,48,1),(4,32,2,0),(4,4,1),(20,2,2,270),(4,4,0),(4,32,0))}')
c.ent(f'guard id=pool at=55,32 route={R((55,32,2,90),(55,6,2,180),(40,2,2,270),(55,6,0),(58,33,2,270),(48,45,1),(58,33,0))}')
c.ent('guard id=foyer at=33,32 facing=90 look="90,180,0"')
c.ent('guard id=secchief at=24,25 facing=90 look="90,0" type=elite weapon=smg key=red')
c.ent(f'guard id=gallery at=26,18 route={R((26,18,2),(34,18,2),(34,21,0),(26,21,1))}')
c.ent(f'guard id=ballrm at=40,31 route={R((40,31,2,0),(51,31,1),(51,19,2,180),(40,19,1))}')
c.ent(f'guard id=kitchen at=12,20 route={R((12,20,2),(19,21,1),(15,24,0),(19,32,2,180),(10,32,1),(10,24,0))}')
c.ent(f'guard id=northc at=23,12 route={R((23,12,3,270),(23,6,3,270))}')
c.ent(f'guard id=cellar at=69,5 route={R((69,5,2),(69,10,0),(75,11,2,0),(85,11,2,90),(76,11,0),(69,10,0))}')
c.ent(f'guard id=gatehouse at=38,56 route={R((38,56,4),(34,55,0),(27,57,3,90),(34,55,0))}')

# Civilians
c.ent(f'civilian id=chef at=17,19 type=staff route={R((17,19,6,90),(11,19,4),(19,15,5,90))}')
c.ent(f'civilian id=maid at=45,10 type=staff route={R((45,10,5),(46,15,0),(40,24,1),(34,20,0),(28,11,6),(34,20,0),(46,15,0))}')
c.ent(f'civilian id=guest1 at=46,21 type=guest route={R((46,21,8,0),(48,24,6),(44,27,7))}')
c.ent(f'civilian id=guest2 at=43,27 type=guest route={R((43,27,10,90),(48,19,6),(51,24,6))}')
c.ent(f'civilian id=guest3 at=33,19 type=guest route={R((33,19,9),(36,24,5),(26,16,7))}')

# Pickups
c.ent('pickup item=medkit at=24,32')
c.ent('pickup item=ammo amount=24 at=40,56')
c.ent('pickup item=cash amount=300 at=37,6')
c.ent('pickup item=cash amount=1500 at=83,16')
c.ent('pickup item=throwing_knives amount=2 at=66,20')
c.ent('pickup item=armor amount=35 at=56,56')

# ---------------------------------------------------------------- missions
# Mission 1: The Host - Moretti moves between party, study call and bedroom; bodyguard follows him.
m1 = 'mansion_host'
c.ent(f'target id=moretti name="Victor Moretti" at=46,25 escape=30,58 route={R((46,25,10,180,"Greeting_guests"),(47,20,8,0,"Drinks_at_the_bar"),(30,18,4,None,"Admiring_art"),(17,9,14,90,"Phone_call"),(28,11,9,None,"Resting"),(34,7,6,None,"Freshening_up"),(30,18,3),(44,27,10,None,"Mingling"))}', m1)
c.ent('guard id=bodyguard at=46,23 type=elite weapon=smg follow=moretti', m1)

# Mission 2: Family Secrets - retrieve the ledger from the vault and silence the accountant in the cellar.
m2 = 'mansion_ledger'
c.ent(f'target id=accountant name="Paolo Greco" at=74,15 escape=10,15 weapon=pistol route={R((74,15,12,0,"Counting_money"),(68,20,6,None,"Checking_stock"),(76,11,8,None,"Tasting_wine"),(74,15,6))}', m2)
c.ent('pickup item=intel id=ledger name="Moretti Ledger" at=81,20', m2)
c.ent(f'guard id=cellar2 at=73,22 type=elite weapon=rifle route={R((73,22,3,0),(76,21,3),(67,21,2),(73,14,2))}', m2)
c.ent(f'guard id=hall2 at=36,14 route={R((36,14,3),(26,14,1),(23,8,3,270),(26,14,0))}', m2)

if __name__ == '__main__':
    out = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', 'Resources', 'Maps', 'mansion.txt')
    c.save(out)
    if '-v' in sys.argv:
        c.show()
    print('saved', os.path.normpath(out))

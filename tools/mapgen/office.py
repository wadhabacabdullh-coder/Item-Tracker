"""Map 3: Kessler Tower - a downtown office building.

Three levels drawn side by side and linked by stairs / elevator:
  * Street + lobby (west)        * Executive office floor (north-east)        * Rooftop with helipad (south-east)
The elevator needs the blue access card; the stairwell is open but watched.
"""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from canvas import Canvas

c = Canvas(100, 60)
R = Canvas.route

c.meta.update(id='office', name='Kessler Tower', theme='office', ambient='0.22',
              description='A glass tower in the financial district. Three levels, open-plan offices and a rooftop helipad.')

# ---------------------------------------------------------------- street, alley and service yard
c.room(0, 19, 45, 59, floor='"')
c.fill(1, 54, 44, 58, '.')                  # road (asphalt)
for x in (3, 14, 31, 40):
    c.block(x, 55, ["ggg", "ggg"])
c.block(6, 20, ["xx", "xx"]); c.put(1, 30, 'e'); c.block(1, 40, ["x", "x"])
c.block(38, 20, ["ggg"]); c.put(44, 21, 'e')
for x in (8, 20, 34):
    c.put(x, 52, 'T')

# ---------------------------------------------------------------- lobby (ground floor)
c.room(4, 22, 44, 51, floor='_')
c.fill(21, 51, 24, 51, 'D')                # glass entrance doors
for x in (8, 12, 16, 29, 33, 37, 41):
    c.put(x, 51, 'W')
# Reception desk & waiting area
c.block(19, 41, ["uuuuuuuu"]); c.block(19, 42, ["```c`c``"])
c.block(10, 46, ["ss`ss"]); c.block(10, 48, ["`tt`"]); c.block(33, 46, ["ss`ss"]); c.block(34, 48, ["tt"])
for (x, y) in [(18, 44), (27, 44), (18, 49), (27, 49)]:
    c.put(x, y, 'i')
c.put(5, 50, 'p'); c.put(43, 50, 'p')
# Security desk (east) with monitors
c.vline(36, 36, 43, 'w'); c.put(36, 40, '_')
c.block(38, 37, ["qqqq", "`cc`"]); c.put(43, 37, 'f')
# Cafe (west)
c.vline(14, 23, 36); c.hline(4, 14, 36)
c.put(14, 32, 'D'); c.put(9, 36, 'D')
c.fill(5, 23, 13, 35, ':')
c.block(5, 23, ["uuuuu"]); c.put(12, 23, 'j'); c.put(13, 23, 'j')
c.block(6, 27, ["t`t`t", "c`c`c", "`````", "t`t`t", "c`c`c"])
# Restrooms
c.room(14, 22, 22, 30, floor='_')
c.put(18, 30, 'D')
c.block(15, 23, ["o`o`o", "`````", "nnn"])
# Elevators & stairwell (north)
c.room(22, 22, 36, 30, floor='.')
c.put(29, 30, 'D')
c.block(23, 23, ["==", "=="])              # elevator car (lobby)
c.vline(26, 23, 29); c.put(26, 26, '.')
c.put(35, 29, 'p'); c.hline(30, 34, 23, 'h')
c.room(36, 22, 44, 30, floor='.')
c.put(40, 30, 'D')
c.block(41, 23, ["===", "==="])            # stairwell up
c.put(37, 23, 'f')
c.put(4, 44, 'D')                          # alley service door
c.put(35, 22, 'D')                         # service yard door

# ---------------------------------------------------------------- executive floor (north-east)
c.room(48, 1, 99, 40, floor=';')
# Stairwell & elevator lobby (south-west of the floor)
c.room(48, 32, 62, 40, floor='.')
c.block(49, 37, ["===", "==="])            # stairs down to the lobby
c.block(49, 33, ["==", "=="])              # stairs up to the roof
c.put(55, 32, 'D')
c.room(62, 34, 76, 40, floor='.')
c.block(66, 38, ["==", "=="])              # elevator car
c.put(69, 34, 'D'); c.put(62, 37, 'D')
c.put(75, 39, 'p')
# Open-plan cubicles (centre)
c.fill(49, 13, 98, 31, ';')
for y in (15, 20, 25):
    for x in (52, 60, 68, 76, 84):
        c.block(x, y, ["wwwww", "wk`kw", "w```w"])
c.put(98, 22, 'W'); c.put(98, 27, 'W')
c.block(92, 29, ["ff`ff"]); c.put(49, 13, 'p'); c.put(92, 14, 'p')
# North row: exec offices with glass walls, meeting room, CEO corner office, server room
c.hline(48, 99, 12)
c.vline(58, 1, 12); c.vline(68, 1, 12); c.vline(84, 1, 12)
for x in (51, 54, 61, 64, 72, 76, 80, 88, 94):
    c.put(x, 12, 'W')
# Office A
c.fill(49, 2, 57, 11, ':'); c.block(51, 4, ["kkk", "`c`"]); c.put(49, 2, 'f'); c.put(57, 2, 'p'); c.put(56, 12, 'D')
# Office B
c.fill(59, 2, 67, 11, ':'); c.block(61, 4, ["kkk", "`c`"]); c.put(67, 2, 'f'); c.put(59, 11, 'C'); c.put(66, 12, 'D')
# Meeting room
c.fill(69, 2, 83, 11, '.'); c.block(71, 5, ["cccccccccc", "tttttttttt", "tttttttttt", "cccccccccc"])
c.put(83, 2, 'q'); c.put(69, 2, 'p'); c.put(78, 12, 'D')
# CEO office (red lock) with private bath
c.fill(85, 2, 98, 11, ';'); c.put(90, 12, 'M')
c.block(88, 4, ["kkkk", "`cc`"]); c.block(94, 8, ["ss", "ss"]); c.hline(85, 90, 2, 'h')
c.vline(95, 1, 6); c.hline(95, 99, 6); c.put(97, 6, 'D'); c.fill(96, 2, 98, 5, '_'); c.block(96, 2, ["o`n"])
c.put(99, 9, 'W')
# Server room + copy room + kitchenette (south-east)
c.room(76, 32, 99, 40, floor='.')
c.vline(86, 32, 40); c.vline(92, 32, 40)
c.put(80, 32, 'L'); c.put(89, 32, 'D'); c.put(95, 32, 'D')
for y in (34, 36, 38):
    c.hline(78, 84, y, 'r')
c.block(87, 34, ["mm", "``", "ff"]); c.put(91, 39, 'x')
c.fill(93, 33, 98, 39, '_'); c.block(93, 34, ["uuuu"]); c.block(94, 37, ["tt"]); c.put(98, 33, 'j')

# ---------------------------------------------------------------- rooftop (south-east)
c.room(48, 42, 99, 59, floor='.')
c.room(48, 42, 56, 49, floor='.')           # stair hut
c.block(49, 43, ["==", "=="])
c.put(56, 46, 'D')
c.fill(78, 46, 95, 57, '"')                # helipad
c.fill(84, 50, 89, 53, ',')                # H mark (painted)
for (x, y) in [(60, 44), (60, 50), (66, 44), (66, 50), (72, 44)]:
    c.block(x, y, ["mmm", "mmm"])
c.block(50, 53, ["xx", "xx"]); c.block(62, 56, ["ee"]); c.put(74, 55, 'x')
c.block(96, 43, ["mm", "mm"])

# ---------------------------------------------------------------- entities
c.ent('spawn at=2,57')
c.ent('extract rect=1,53,3,6 label="Street"')
c.ent('extract rect=80,48,14,8 label="Helipad"')
c.ent('stairs a=42,24 b=50,38')
c.ent('stairs a=50,34 b=50,44')
c.ent('stairs a=24,24 b=67,39 lock=blue label="Elevator"')

for name, rect in [("Street", "1,52,44,7"), ("Alley", "1,20,3,32"), ("Service Yard", "4,20,41,2"),
                   ("Lobby", "15,31,21,20"), ("Cafe", "5,23,9,13"), ("Restrooms", "15,23,7,7"),
                   ("Elevators", "23,23,13,7"), ("Stairwell", "37,23,7,7"), ("Security Desk", "37,36,7,8"),
                   ("Stairwell", "49,33,13,7"), ("Elevator Lobby", "63,35,13,5"), ("Open Office", "49,13,50,19"),
                   ("Office A", "49,2,9,10"), ("Office B", "59,2,9,10"), ("Meeting Room", "69,2,15,10"),
                   ("CEO Office", "85,2,14,10"), ("Server Room", "77,33,9,7"), ("Copy Room", "87,33,5,7"),
                   ("Kitchenette", "93,33,6,7"), ("Rooftop", "49,43,50,16"), ("Helipad", "78,46,18,12")]:
    c.ent(f'zone name="{name}" rect={rect}')

lights = [
    (12, 53, 6, 0.9, 'ffcf80', None), (27, 53, 6, 0.9, 'ffcf80', None), (41, 53, 6, 0.9, 'ffcf80', None),
    (2, 28, 3, 0.5, 'ffa050', True), (23, 45, 8, 1.0, 'f0f4ff', None), (37, 45, 5, 0.8, 'f0f4ff', None),
    (9, 29, 6, 0.9, 'ffe0b0', None), (18, 26, 4, 0.7, 'e8f0ff', None), (29, 26, 5, 0.8, 'f0f4ff', None),
    (40, 26, 4, 0.6, 'e8f0ff', None), (40, 40, 4, 0.8, '80c0ff', None),
    (55, 36, 5, 0.6, 'e8f0ff', None), (69, 37, 5, 0.7, 'f0f4ff', None),
    (58, 22, 7, 0.8, 'f0f4ff', None), (74, 22, 7, 0.8, 'f0f4ff', None), (90, 22, 7, 0.8, 'f0f4ff', None),
    (53, 6, 5, 0.7, 'ffe0b0', None), (63, 6, 5, 0.7, 'ffe0b0', None), (76, 6, 6, 0.9, 'f0f4ff', None), (91, 6, 6, 0.9, 'ffd9a0', None),
    (81, 36, 5, 0.7, '6090ff', None), (95, 36, 4, 0.7, 'fff0d0', None),
    (86, 51, 8, 0.9, 'ff5050', None), (64, 48, 5, 0.4, 'ffcf80', True), (52, 46, 3, 0.6, 'ffe080', None),
]
for (x, y, r, i, col, fl) in lights:
    c.ent(f'light at={x},{y} r={r} i={i} color={col}' + (' flicker=1' if fl else ''))

for (x, y, facing, sweep) in [(44, 50, 135, 50), (48, 13, 315, 60), (98, 31, 135, 50), (61, 39, 45, 40)]:
    c.ent(f'camera at={x},{y} facing={facing} sweep={sweep} group=cams')
c.ent('terminal at=39,37 action=cameras group=cams label="Security console"')
c.ent('terminal id=mainframe at=84,39 action=download time=5 label="Mainframe"')
c.ent('powerbox at=37,23 group=lobby label="Fuse box"')
c.ent('distraction at=87,34 kind=printer')
c.ent('distraction at=12,23 kind=vending')

# Guards
c.ent('guard id=door at=22,49 facing=270 look="270,0,180"')
c.ent(f'guard id=lobby at=30,44 route={R((30,44,2),(15,44,2,180),(15,38,1),(30,38,2))}')
c.ent('guard id=secdesk at=39,38 facing=270 look="270,180" key=blue')
c.ent(f'guard id=alley at=2,40 route={R((2,40,2),(2,22,3,90),(20,21,2),(2,22,0))}')
c.ent(f'guard id=street at=40,52 route={R((40,52,3,270),(4,52,2,180))}')
c.ent(f'guard id=stairs at=58,38 route={R((58,38,3),(58,33,2),(66,36,2),(58,36,1))}')
c.ent(f'guard id=floorw at=50,30 route={R((50,30,2),(50,14,2),(66,18,0),(58,30,1))}')
c.ent(f'guard id=floore at=97,14 route={R((97,14,2),(97,30,2),(82,24,1),(90,14,1))}')
c.ent('guard id=ceo at=89,13 facing=90 type=elite weapon=smg look="90,0,180"')
c.ent(f'guard id=roof at=58,57 type=elite weapon=rifle route={R((58,57,3),(76,57,2),(76,44,2),(58,53,1))}')

c.ent(f'civilian id=worker1 at=55,17 type=worker route={R((55,17,12,None,"Typing"),(63,22,6),(95,36,8,None,"Coffee"),(55,17,0))}')
c.ent(f'civilian id=worker2 at=79,22 type=worker route={R((79,22,10),(89,35,7,None,"Copying"),(71,27,9))}')
c.ent(f'civilian id=worker3 at=63,27 type=worker route={R((63,27,14,None,"On_a_call"),(71,17,8),(55,27,6))}')
c.ent(f'civilian id=recept at=22,42 type=worker route={R((22,42,15,270,"Answering_phones"),(9,31,6,None,"Getting_coffee"),(22,42,0))}')
c.ent(f'civilian id=visitor at=12,45 type=guest route={R((12,45,12),(30,47,8),(20,33,5))}')

c.ent('pickup item=keycard_blue at=6,34')
c.ent('pickup item=medkit at=19,25')
c.ent('pickup item=ammo amount=30 at=97,35')
c.ent('pickup item=cash amount=600 at=86,4')
c.ent('pickup item=armor amount=50 at=51,52')
c.ent('pickup item=weapon_shotgun at=37,24')

# ---------------------------------------------------------------- missions
m1 = 'office_cfo'
c.ent(f'target id=kessler name="Marcus Kessler" at=90,8 escape=2,57 key=red route={R((90,8,14,270,"Signing_contracts"),(76,10,10,None,"Board_meeting"),(80,19,4,None,"Walking_the_floor"),(96,36,6,None,"Coffee"),(66,5,6,None,"Visiting_Office_B"),(90,8,6))}', m1)
c.ent('guard id=cfoguard at=88,9 type=elite weapon=smg follow=kessler', m1)

m2 = 'office_breach'
c.ent(f'target id=exec1 name="Dana Whitmore" at=73,9 escape=2,57 route={R((73,9,20,270,"Board_meeting"),(62,5,10,None,"Private_call"),(73,9,20,270))}', m2)
c.ent(f'target id=exec2 name="Victor Hale" at=80,4 escape=90,50 route={R((80,4,20,90,"Board_meeting"),(95,36,8,None,"Coffee"),(80,4,20,90))}', m2)
c.ent('guard id=meetguard at=77,11 type=elite weapon=smg facing=270 look="270,0,180"', m2)
c.ent('pickup item=keycard_red at=66,3', m2)

if __name__ == '__main__':
    out = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', 'Resources', 'Maps', 'office.txt')
    c.save(out)
    if '-v' in sys.argv:
        c.show()
    print('saved', os.path.normpath(out))

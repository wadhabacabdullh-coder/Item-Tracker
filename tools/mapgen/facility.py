"""Map 2: Helix Labs - an underground research facility.

Routes past the blue-keycard checkpoint:
  * take the keycard from the checkpoint guard / dock office
  * crawl the vent from the dock storage into the security room
Security cameras can be disabled from the security room terminal.
"""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from canvas import Canvas

c = Canvas(92, 56)
R = Canvas.route

c.meta.update(id='facility', name='Helix Labs', theme='facility', ambient='0.18',
              description='A secret lab complex under an industrial park. Cameras, keycards and ventilation shafts.')

# ---------------------------------------------------------------- west: loading dock, dock office, dock storage
c.room(0, 19, 14, 35, floor='"')
c.block(2, 21, ["ggg", "ggg", "ggg", "ggg"])
c.block(2, 29, ["ggg", "ggg", "ggg", "ggg"])
c.block(10, 20, ["xx", "xx"])
c.block(11, 32, ["xxx"])
c.put(13, 26, 'e')
# Dock office (north of the dock)
c.room(0, 12, 12, 19, floor='.')
c.put(6, 19, 'D')
c.block(2, 13, ["kk`f", "`c``"])
c.put(11, 13, 'f'); c.put(1, 18, 'C'); c.put(6, 12, 'W')
# Dock storage (south) with the vent entrance to the security room
c.room(0, 35, 14, 46, floor='.')
c.put(7, 35, 'D')
c.block(1, 37, ["xx`xx`x", "xx`xx`x", "```````", "x`xx`xx", "x`xx`xx"])
c.block(1, 43, ["hhhh"]); c.put(13, 45, 'f')
c.hline(14, 24, 38, 'v')                   # vent: dock storage -> security room
c.put(13, 38, '.')

# ---------------------------------------------------------------- checkpoint
c.room(14, 22, 24, 33, floor='.')
c.block(16, 23, ["qq`q", "`c``"])
c.hline(15, 18, 29, 'w'); c.put(19, 29, '.')   # booth partition
c.put(16, 32, 'j'); c.put(23, 23, 'f')
c.put(24, 27, 'L'); c.put(24, 28, 'L')         # blue security doors
c.put(14, 27, 'D')                             # dock -> checkpoint

# ---------------------------------------------------------------- main corridor
c.room(24, 25, 88, 30, floor='.')
c.put(24, 27, 'L'); c.put(24, 28, 'L')
for x in (38, 54, 71, 86):
    c.put(x, 26, 'p')
c.put(60, 29, 'j')
# East freight elevator (second extraction)
c.room(88, 24, 91, 31, floor='"')
c.put(88, 27, 'D'); c.put(88, 28, 'D')

# ---------------------------------------------------------------- north wing
# Biolab
c.room(24, 8, 44, 25, floor='_')
c.put(34, 25, 'D')
c.block(26, 10, ["mm``~~~``mm", "mm``~~~``mm"])
c.block(26, 15, ["tttt``tttt"])
c.block(26, 19, ["tttt``tttt"])
c.block(26, 23, ["uuu"]); c.put(43, 9, 'f'); c.put(43, 23, 'f')
c.put(30, 8, 'v'); c.vline(30, 4, 7, 'v')     # vent grate in the lab ceiling
# Server room (blue door)
c.room(44, 8, 56, 25, floor='.')
c.put(50, 25, 'L')
for y in (10, 13, 16, 19):
    c.hline(46, 49, y, 'r'); c.hline(51, 54, y, 'r')
c.block(46, 22, ["qq`"]); c.put(55, 23, 'f')
c.put(44, 21, 'D')                          # lab <-> server side door
# Vent along the top to the director's office
c.hline(30, 55, 4, 'v')
# Director's office + reception
c.room(56, 2, 74, 17, floor=';')
c.block(62, 5, ["kkkk", "`cc`"])
c.block(58, 12, ["ss", "ss"]); c.put(58, 11, 't')
c.hline(68, 73, 3, 'h'); c.put(73, 9, 'C'); c.put(57, 3, 'p'); c.put(73, 16, 'p')
c.put(65, 17, 'D')
c.room(56, 17, 74, 25, floor='.')
c.block(62, 19, ["uuuu"]); c.put(62, 20, '.')
c.block(57, 22, ["sss"]); c.put(73, 18, 'p')
c.put(65, 25, 'D')
c.put(64, 2, 'W')
c.put(56, 4, 'v')                           # vent exit into the director's office
# Break room & bathrooms
c.room(74, 8, 88, 17, floor='_')
c.block(76, 10, ["tt``tt", "cc``cc"]); c.block(84, 9, ["jj"]); c.put(76, 16, 'u'); c.put(77, 16, 'u')
c.room(74, 17, 88, 25, floor='_')
c.put(80, 17, 'D')
c.hline(74, 88, 21); c.put(80, 21, 'D')
c.block(75, 18, ["o`o`o`o`o"]); c.block(75, 24, ["nnnn"])
c.put(84, 25, 'D')
c.put(74, 12, 'D')                          # office <-> break room

# ---------------------------------------------------------------- south wing
# Security control room (camera terminal) - vent arrives on its west wall
c.room(24, 30, 38, 40, floor='.')
c.put(31, 30, 'D')
c.block(26, 32, ["qqqqq", "`ccc`"])
c.put(37, 31, 'f'); c.put(37, 32, 'f'); c.put(25, 39, 'C')
c.put(24, 38, 'v')
# Barracks
c.room(38, 30, 52, 40, floor='.')
c.put(45, 30, 'D')
c.block(40, 32, ["bb`bb`bb", "bb`bb`bb"])
c.block(40, 36, ["bb`bb`bb", "bb`bb`bb"])
c.hline(39, 43, 39, 'f'); c.hline(47, 51, 39, 'f')
c.put(51, 34, 'C')
# Warehouse
c.room(24, 40, 52, 54, floor='.')
c.put(31, 40, 'D'); c.put(45, 40, 'D')
for (x, y) in [(26, 42), (26, 46), (26, 50), (33, 43), (33, 48), (40, 44), (40, 49), (46, 46)]:
    c.block(x, y, ["xxx", "xxx"])
c.hline(26, 50, 53, 'h'); c.put(38, 52, 'g'); c.put(50, 43, 'e')
# Generator room
c.room(52, 30, 66, 46, floor='_')
c.put(59, 30, 'D'); c.put(52, 44, 'D')
c.block(54, 33, ["mmm``mmm", "mmm``mmm"])
c.block(54, 39, ["mmm``mmm", "mmm``mmm"])
c.block(64, 32, ["e", "e", "`", "e"]); c.put(55, 44, 'e'); c.put(63, 44, 'e')
# Containment lab (red door) with observation windows
c.room(66, 30, 88, 46, floor='_')
c.put(76, 30, 'M')
c.hline(67, 87, 36, 'W'); c.put(70, 36, 'D')
c.block(68, 32, ["qqq"]); c.block(82, 32, ["qq"])
c.fill(74, 39, 80, 43, '_'); c.block(75, 40, ["mmmmm"]); c.block(76, 42, ["t`t"])
c.put(87, 45, 'f'); c.put(67, 45, 'f')
c.put(66, 38, 'M')                          # generator <-> containment maintenance door (red)

# ---------------------------------------------------------------- entities
c.ent('spawn at=7,27')
c.ent('extract rect=1,26,1,4 label="Loading dock"')
c.ent('extract rect=89,25,2,6 label="Freight elevator"')

for name, rect in [("Loading Dock", "1,20,13,15"), ("Dock Office", "1,13,11,6"), ("Dock Storage", "1,36,13,10"),
                   ("Checkpoint", "15,23,9,10"), ("Main Corridor", "25,26,63,4"), ("Freight Elevator", "89,25,2,6"),
                   ("Biolab", "25,9,19,16"), ("Server Room", "45,9,11,16"), ("Director's Office", "57,3,17,14"),
                   ("Reception", "57,18,17,7"), ("Break Room", "75,9,13,8"), ("Restrooms", "75,18,13,7"),
                   ("Security Control", "25,31,13,9"), ("Barracks", "39,31,13,9"), ("Warehouse", "25,41,27,13"),
                   ("Generator Room", "53,31,13,15"), ("Containment Lab", "67,31,21,15"), ("Ventilation", "14,4,43,35")]:
    c.ent(f'zone name="{name}" rect={rect}')

lights = [
    (7, 24, 6, 0.8, 'ffd080', None), (7, 31, 5, 0.6, 'ffd080', True), (6, 15, 4, 0.7, 'e0f0ff', None),
    (7, 41, 4, 0.5, 'ffb060', True), (19, 26, 5, 1.0, 'e0f0ff', None),
    (30, 27, 5, 0.8, 'd0e8ff', None), (46, 27, 5, 0.8, 'd0e8ff', None), (62, 27, 5, 0.8, 'd0e8ff', None), (78, 27, 5, 0.8, 'd0e8ff', None),
    (34, 12, 6, 0.9, '80ffb0', None), (34, 21, 6, 0.8, 'd0ffe0', None), (50, 16, 7, 0.7, '6090ff', None),
    (65, 9, 6, 0.9, 'ffd9a0', None), (65, 21, 5, 0.8, 'ffe0c0', None), (81, 12, 5, 0.8, 'fff0d0', None), (81, 21, 4, 0.6, 'e0f0ff', None),
    (31, 35, 5, 0.9, '80c0ff', None), (45, 36, 6, 0.5, 'ffc080', None), (32, 47, 6, 0.6, 'ffd080', True), (45, 48, 5, 0.5, 'ffd080', None),
    (59, 38, 6, 0.8, 'ff8040', True), (77, 33, 6, 0.9, 'd0e8ff', None), (77, 41, 5, 1.0, 'ff4060', None), (89, 28, 3, 0.8, 'ffe080', None),
]
for (x, y, r, i, col, fl) in lights:
    c.ent(f'light at={x},{y} r={r} i={i} color={col}' + (' flicker=1' if fl else ''))

# Security cameras (group 'cams' is switched off from the security terminal)
for (x, y, facing, sweep) in [(15, 33, 45, 50), (25, 29, 0, 60), (87, 29, 180, 60), (43, 24, 225, 50), (65, 45, 135, 50), (13, 20, 315, 40)]:
    c.ent(f'camera at={x},{y} facing={facing} sweep={sweep} group=cams')
c.ent('terminal at=28,32 action=cameras group=cams label="Security terminal"')
c.ent('powerbox at=55,31 group=generator label="Breaker panel"')
c.ent('distraction at=84,10 kind=vending')
c.ent('distraction at=6,13 kind=radio')

# Guards
c.ent('guard id=checkpt at=20,27 facing=180 look="180,270,90" key=blue')
c.ent(f'guard id=dock at=9,23 route={R((9,23,2),(9,33,2,270),(12,28,2,0))}')
c.ent(f'guard id=corrw at=30,28 route={R((30,28,2,0),(55,28,1),(55,26,2,90),(30,27,1))}')
c.ent(f'guard id=corre at=85,27 route={R((85,27,2,180),(58,27,1),(58,29,2,270),(85,29,1))}')
c.ent(f'guard id=lab at=36,17 route={R((36,17,3),(42,12,2),(36,22,2),(28,18,3,180))}')
c.ent(f'guard id=server at=50,22 route={R((50,22,2),(50,11,2),(45,21,0),(47,17,2))}')
c.ent('guard id=office at=60,21 facing=0 look="0,90,270"')
c.ent(f'guard id=barracks at=45,35 type=elite weapon=rifle route={R((45,35,4),(45,38,1),(49,35,3),(41,35,2))}')
c.ent(f'guard id=wareh at=30,45 route={R((30,45,2),(37,51,1),(44,51,2),(47,44,1),(37,42,2))}')
c.ent(f'guard id=gen at=58,37 route={R((58,37,2),(62,43,2),(53,43,1),(58,32,2))}')
c.ent(f'guard id=contain at=72,34 type=elite weapon=smg route={R((72,34,3),(84,34,3),(84,33,1),(70,33,2))}')
c.ent('guard id=secroom at=29,33 facing=90 look="90,270"')

c.ent(f'civilian id=sci1 at=31,13 type=scientist route={R((31,13,8,90,"Running_tests"),(38,18,6),(32,21,5))}')
c.ent(f'civilian id=sci2 at=40,14 type=scientist route={R((40,14,7),(47,21,6,None,"Checking_servers"),(40,20,4))}')
c.ent(f'civilian id=sci3 at=78,11 type=scientist route={R((78,11,9,None,"Coffee_break"),(79,19,6),(71,27,1),(78,11,0))}')
c.ent(f'civilian id=tech at=72,40 type=scientist route={R((72,40,8),(84,40,6),(77,34,5))}')

c.ent('pickup item=keycard_blue at=4,14')
c.ent('pickup item=medkit at=78,23')
c.ent('pickup item=ammo amount=30 at=48,35')
c.ent('pickup item=cash amount=400 at=70,6')
c.ent('pickup item=armor amount=50 at=36,39')
c.ent('pickup item=weapon_smg at=42,34')

# ---------------------------------------------------------------- missions
m1 = 'facility_voss'
c.ent(f'target id=voss name="Dr. Helena Voss" at=36,13 escape=90,28 route={R((36,13,12,90,"Examining_samples"),(48,21,8,None,"Reviewing_data"),(65,9,12,None,"Meeting_the_director"),(79,11,8,None,"Coffee_break"),(62,21,4),(36,20,10,None,"Lab_work"))}', m1)
c.ent('guard id=vossguard at=37,12 type=elite weapon=smg follow=voss', m1)
c.ent('pickup item=intel id=samples name="Pathogen Samples" at=43,11', m1)

m2 = 'facility_prototype'
c.ent(f'target id=kovac name="Chief Anton Kovac" at=30,34 weapon=smg hp=160 key=red escape=7,27 route={R((30,34,10,90,"Watching_monitors"),(45,34,6,None,"Inspecting_barracks"),(59,28,2),(77,34,8,None,"Checking_containment"),(31,28,2))}', m2)
c.ent(f'target id=director name="Director Lin Hale" at=64,9 escape=90,28 route={R((64,9,14,270,"Paperwork"),(60,14,8,None,"Phone_call"),(64,21,5),(80,11,7,None,"Coffee"),(64,9,4))}', m2)
c.ent('pickup item=keycard_red at=71,4', m2)
c.ent('pickup item=intel id=prototype name="Helix Prototype" at=77,42', m2)
c.ent(f'guard id=dirguard at=65,14 type=elite weapon=rifle follow=director', m2)

if __name__ == '__main__':
    out = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', 'Resources', 'Maps', 'facility.txt')
    c.save(out)
    if '-v' in sys.argv:
        c.show()
    print('saved', os.path.normpath(out))

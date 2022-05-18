# SpellPouch
Mod for Pathfinder: Wrath of the Righteous

Index
-----------
* [Disclaimers](#disclaimers)
* [Installation](#installation)
* [Contact](#contact)
* [Ability Groups](#ability-groups)
* [Spell Groups](#spell-groups)
* [Building](#building)
* [Contact](#contact)
* [FAQ](#faq)

Disclaimers
-----------
* This mod will affect your save! Uninstalling might break your save.
* I do not take any responsibility for broken saves or any other damage. Use this software at your own risk.
* Please DON'T REPORT BUGS you encounter to Owlcat Games while mods are active.
* BE AWARE that all mods are WIP and may fail.

Installation
-----------
* You will need [Unity Mod Manager](https://www.nexusmods.com/site/mods/21).
* Follow the installation procedure (tl;dr; select game, select folder, press install).
* Download a release or rebuild your own [https://github.com/Truinto/SpellPouch/releases](https://github.com/Truinto/SpellPouch/releases).
* Switch to the mod tab and drop the zip file into the manager.
* If you have DarkCodex installed, only use version 1.3.0 or higher.

Contact
-----------
@Fumihiko me on the Owlcat Pathfinder discord: [https://discord.com/invite/wotr](https://discord.com/invite/wotr)

Ability Groups
-----------
Ability Groups let you bundle abilities and activatables into a single foldable actionbar slot. They have a black border. \
![example group](/resources/example-group.jpg) \
There are already some predefined groups for class features that are related to each other or use the same resource (like bard songs).
You can place the group on your action bar or open the folding view to place the abilities on your action bar directly. \
The groups are defined in this file: "Mods/SpellPouch/DefGroups.json"

Each group has these properties:
- Title: Name of the group; must be unique
- Description: Text displayed in the group's body; can be empty/null
- Icon: Icon to use for this group; if icon is null, it will display the first active activatable or, if non are active, the first ability's icon
- Guids: a identifier list of all abilities/activatables that are related to that group; if a guid is used in multiple groups, only the first group will apply

You may edit this file and reload the groups with the button in the mod's menu. There is also a button to unlock the groups.
This will display all groups for all characters, even if they have no matching abilities. To add/remove abilities with drag&drop mechanic, hold SHIFT will doing so.
Simply hold shift and drag any ability on the group's icon to add it. \
![add ability](/resources/adding-ability.jpg) \
Unfold a group, hold shift and drag an ability from it onto the map to remove it again. \
![remove ability](/resources/remove-ability.jpg) \
Hold shift and drag an ability onto another ability to create a new group. You will be prompted to give a new unique title name. \
![creating group](/resources/creating-group.jpg) \
To delete a group altogether, hold shift and drag the group onto the map. This will also prompt a confirmation. \
![delete group](/resources/delete-group.jpg) \
You can re-order groups by holding shift and draging an ability either on the left or right side of an existing ability. \
Placing it left, will move it to the left of the target ability. \
![move left](/resources/move-left.jpg) \
Placing it right, will move it to the right of the target ability. \
![move right](/resources/move-right.jpg) \
Changes will affect all party members equally. Remember to disable 'Unlock Groups' again, otherwise you might edit them unintentionally.

Spell Groups
-----------
You can also create Spell Groups. They have a grey border. \
![example spell group](/resources/spell-group.png) \
Spell Groups behave a lot like Ability Groups. But they are unique for each character. 
You can add any spell you like, as long as it can be cast. Variant spells cannot be added. Pick one of the variants instead. \
To cast a spell, unfold it and click on any spell. Or you may click the group itself to cast the first available spell. \
You may also use auto cast. It will automatically advance to the next spell, when you run out of spell slots.

Building
-----------
If you want to compile this project, you will need to download the DarkCodex repository as well.

FAQ
-----------
Q: Why is the placement random? \
A: It's not. It will place it left or right of the target ability base on which side is closer. Unfortunately there isn't a visual indicator, so if you are not aware, it will appear random.



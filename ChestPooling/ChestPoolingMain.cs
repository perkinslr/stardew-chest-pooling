﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StardewValley;
using StardewModdingAPI;
using Newtonsoft.Json;
using System.IO;


//bugs while playing:
/*
    - adding an item to a chest that already has a partial in the open chest and in the remote chest, might duplicate. 
    Need to add a pre-check for the open chest to give it priority. * probably fixed, nope, seems that the isn't full check broke being able to remote add when
    theres a full stack
    - weirdness when item exists in multiple places, possibly only when stacks are mostly full


    - need to come up with a system to properly identify if an item should stay in it's current chest
    if the item matched in the chest is literally the same item, then it should start the move check
    if it's not, abort
*/

namespace ChestPooling
{
    public class ChestPoolingMainClass : Mod
    {
        /// <summary>Initialise the mod.</summary>
        /// <param name="helper">Provides methods for interacting with the mod directory, such as read/writing a config file or custom JSON files.</param>
        public override void Entry(IModHelper helper)
        {
            StardewModdingAPI.Events.PlayerEvents.InventoryChanged += Event_InventoryChanged;
            StardewModdingAPI.Events.GameEvents.LoadContent += Event_LoadContent;
        }



        private void myLog(String theString) { 
            #if DEBUG
            Log.Info(theString);
            #endif

        }
        
        private bool loaded = false;
        

        private void debugThing(object theObject, string descriptor = "")
        {
            String thing = JsonConvert.SerializeObject(theObject, Formatting.Indented,
            new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            });
            File.WriteAllText("debug.json", thing);
            Console.WriteLine(descriptor + "\n"+ thing);
        }

        //there's probably an "onloaded" property somewhere that could be tested instead...
        private void Event_LoadContent(object sender, EventArgs e)
        {
            loaded = true;
        }

        private List<StardewValley.Objects.Chest> getChests()
        {
            if (!loaded) { return null; }
            if (StardewValley.Game1.currentLocation == null) { return null; }

            List<StardewValley.Objects.Chest> chestList = new List<StardewValley.Objects.Chest>();

            //get chests from normal buildings
            foreach (StardewValley.GameLocation location in StardewValley.Game1.locations)
            {
                foreach (KeyValuePair<Vector2, StardewValley.Object> farmObj in location.Objects)
                {
                    if (farmObj.Value is StardewValley.Objects.Chest)
                    {
                        chestList.Add(farmObj.Value as StardewValley.Objects.Chest);
                    }
                }

                //get fridge
                if (location is StardewValley.Locations.FarmHouse)
                {
                    StardewValley.Locations.FarmHouse house = location as StardewValley.Locations.FarmHouse;
                    if(house.fridge != null)
                    {
                        chestList.Add(house.fridge);
                    }
                }
            }
            
            //get stuff inside build buildings
            StardewValley.Farm farm = StardewValley.Game1.getFarm();
            if (farm != null) {
                foreach (StardewValley.Buildings.Building building in farm.buildings)
                {
                    if (building.indoors != null)
                    {
                        foreach (KeyValuePair<Vector2, StardewValley.Object> farmObj in building.indoors.Objects)
                        {
                            if (farmObj.Value is StardewValley.Objects.Chest)
                            {
                                chestList.Add(farmObj.Value as StardewValley.Objects.Chest);
                            }
                        }
                    }
                }
            }

            chestList.RemoveAll(IsIgnored);


            return chestList;
        }

        //chest filter predicate
        private bool IsIgnored(StardewValley.Objects.Chest chest)
        {
            return chest.Name == "IGNORED";
        }

        private StardewValley.Objects.Chest getOpenChest()
        {
            if (StardewValley.Game1.activeClickableMenu == null) { return null; }

            if (StardewValley.Game1.activeClickableMenu is StardewValley.Menus.ItemGrabMenu)
            {
                //myLog("it's an item grab");
                StardewValley.Menus.ItemGrabMenu menu = StardewValley.Game1.activeClickableMenu as StardewValley.Menus.ItemGrabMenu;
                if (menu.behaviorOnItemGrab != null && menu.behaviorOnItemGrab.Target is StardewValley.Objects.Chest)
                {
                    return menu.behaviorOnItemGrab.Target as StardewValley.Objects.Chest;
                }
            }
            else
            {
                myLog("something else" + StardewValley.Game1.activeClickableMenu.GetType().Name);
                if(StardewValley.Game1.activeClickableMenu.GetType().Name == "ACAMenu")
                {
                    dynamic thing = (dynamic)StardewValley.Game1.activeClickableMenu;
                    if(thing != null && thing.chestItems != null)
                    {
                        myLog("woo, survived");
                        StardewValley.Objects.Chest aChest = new StardewValley.Objects.Chest(true);
                        aChest.items = thing.chestItems;
                        return aChest;
                    }
                }
                
                //debugThing(StardewValley.Game1.activeClickableMenu);
            }
            return null;
        }

        private bool isExactItemInChest(StardewValley.Item sourceItem, List<StardewValley.Item> items)
        {
            foreach (StardewValley.Item item in items)
            {
                if (item == sourceItem) { return true; }
            }
            return false;
        }

        // stackSizeOffset, a value to subtract before the stacksize comparison, basically just exists for the "openChest" case, where the item has already been added
        private StardewValley.Item matchingItemInChest(StardewValley.Item sourceItem, List<StardewValley.Item> items, int stackSizeOffset = 0)
        {
            foreach (StardewValley.Item item in items)
            {
                //weirdly, this is an equals check
                //if (sourceItem.canStackWith(item) && (item.Stack - stackSizeOffset) < item.maximumStackSize() && item.Stack - stackSizeOffset > 0)
                if (sourceItem.canStackWith(item) && item.Stack < item.maximumStackSize() && item != sourceItem)
                {
                    return item;
                }
            }
            return null;
        }

        //method is poorly named
        private StardewValley.Objects.Chest QueryChests(List<StardewValley.Objects.Chest> chestList, StardewValley.Item itemRemoved)
        {
            //Log.Info("queryStarted");
            StardewValley.Objects.Chest openChest = getOpenChest();
            StardewValley.Objects.Chest chestWithStack = null;
            StardewValley.Item itemToAddTo = null;
            bool hasFoundCurrentChest = false;

            //likely in some other menu
            if (openChest == null) { return null; }
            //Log.Info("openChest isn't null");
            //the place where it went is fine
            if (!isExactItemInChest(itemRemoved, openChest.items)){
                //Log.Info("item in open chest, aborting");
                return null;
            }
           // Log.Info("isn't in the current chest");

            foreach (StardewValley.Objects.Chest chest in chestList)
            {
                if(chest.items == openChest.items)
                {
                    hasFoundCurrentChest = true;
                    continue;
                }

                //found something, don't bother going any further
                //consider adding another check that completely bails if both the open and "withStack" chest is found
                if (chestWithStack != null) { continue; }

                StardewValley.Item item = matchingItemInChest(itemRemoved, chest.items);
                if(item != null)
                {
                    chestWithStack = chest;
                    itemToAddTo = item;
                }
            }

            //user probably just threw away the item
            //could probably remove this check as a "cheat" to allow remote deposit...
            if (openChest == null || !hasFoundCurrentChest)
            {
                return null;
            }
            //Log.Info("current chest was found");

            if (chestWithStack != null)
            {
                //Log.Info("chestWithStack isn't null");
                if (openChest.items.Count > 0 && chestWithStack.items.Count > 0)
                {
                    //Log.Info("open chest first item: " + openChest.items.First().Name);
                    //Log.Info("target chest first item: " + chestWithStack.items.First().Name);
                }

                int newStackSize = newStackSize = itemToAddTo.Stack + itemRemoved.Stack;

                //resize it in the chest it was placed in
                if (newStackSize > itemRemoved.maximumStackSize())
                {
                    //Log.Info("stack maxed");
                    myLog("stack maxed for " + itemToAddTo.Name);
                    itemRemoved.Stack = newStackSize - itemRemoved.maximumStackSize();
                    itemToAddTo.Stack = itemToAddTo.maximumStackSize();
                }
                //actually do things
                else
                {
                    itemToAddTo.addToStack(itemRemoved.Stack);
                    myLog(itemToAddTo.Name + " new size: " + newStackSize);
                    openChest.items.Remove(itemRemoved);
                    openChest.clearNulls();
                    Game1.activeClickableMenu = (StardewValley.Menus.IClickableMenu)new StardewValley.Menus.ItemGrabMenu(openChest.items, false, true, new StardewValley.Menus.InventoryMenu.highlightThisItem(StardewValley.Menus.InventoryMenu.highlightAllItems), new StardewValley.Menus.ItemGrabMenu.behaviorOnItemSelect(openChest.grabItemFromInventory), (string)null, new StardewValley.Menus.ItemGrabMenu.behaviorOnItemSelect(openChest.grabItemFromChest), false, true, true, true, true, 1, (Item)openChest, -1, (object)null);
                    //openChest.grabItemFromChest(itemRemoved, StardewModdingAPI.Entities.SPlayer.CurrentFarmer);
                }
            }

            return null;
        }

        //e is a thing that contains "Inventory", "Added" and "Removed" properties, not yet sure what object that corresponds to
        private void Event_InventoryChanged(object sender, EventArgs e)
        {
            if (!loaded) { return; }
            if(StardewValley.Game1.currentLocation == null) { return; }

            //the real event, might be necessary to determine what item was placed where
            StardewModdingAPI.Events.EventArgsInventoryChanged inventoryEvent = (StardewModdingAPI.Events.EventArgsInventoryChanged)e;

            if(inventoryEvent.Removed.Count == 0) { return; }

            List<StardewValley.Objects.Chest>  chestList = getChests();
            if (chestList == null) { return; }


            QueryChests(chestList, inventoryEvent.Removed.First().Item);
        }
    }
}

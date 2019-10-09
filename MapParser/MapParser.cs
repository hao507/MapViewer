﻿#region copyright
/*
Copyright 2015 Govind Mukundan

This file is part of MapViewer.

MapViewer is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

MapViewer is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with MapViewer.  If not, see <http://www.gnu.org/licenses/>.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MapViewer
{

    public class Section
    {
        public string Name { get; set; }
        public UInt64 Address { get; set; }
        public UInt64 Size { get; set; }
        public List<Symbol> Symbols { get; set; } // lets just consider that each section has a lot of symbols
        public List<Module> Modules { get; set; }
        public UInt32 FillBytes { get; set; }


        public Section(string name, UInt64 la, UInt64 siz)
        {
            Name = name;
            Address = la;
            Size = siz;
            Symbols = new List<Symbol>();
            Modules = new List<Module>();
            FillBytes = 0;
        }
    }
    public class Symbol
    {
        public string SymbolName { get; set; }
        public string FileName { get; set; }
        public string ModuleName { get; set; }
        public UInt64 LoadAddress { get; set; }
        public UInt32 Size { get; set; }
        public string SectionName{ get; set; }
        public int GlobalScope { get; set; } // For now we have Global, Static and Hidden
        static public int TYPE_GLOBAL = 1;
        static public int TYPE_STATIC = 0;
        static public int TYPE_HIDDEN = 2;

        public Symbol(string sym, string file, UInt64 la, UInt32 siz, string section)
        {
            SymbolName = sym;
            ModuleName = file;
            LoadAddress = la;
            Size = siz;
            SectionName = section;

            // Special for newlib, module name turns out to be of the form ..lib/libc.a(lib_a-rget.o), while dwarf file name = rget.c, so we takeout all "lib_a" prefix
            if (file.Contains("lib_a-"))
                file = file.Replace("lib_a-", String.Empty);

            Match m2 = Regex.Match(file, @"[^\/\\(]+(\.o|\.c)$");
            FileName = m2.ToString();
        }

        public void GetFileFromModuleName(string mod)
        {
            // Special for newlib, module name turns out to be of the form ..lib/libc.a(lib_a-rget.o), while dwarf file name = rget.c, so we takeout all "lib_a" prefix
            if (mod.Contains("lib_a-"))
                mod = mod.Replace("lib_a-", String.Empty);

            Match m2 = Regex.Match(mod, @"[^\/\\(]+(\.o|\.c)$");
            FileName = m2.ToString();
        }
    }

    public class Module
    {
        public string ModuleName { get; set; }
        public UInt32 TextSize { get; set; }
        public UInt32 BSSSize{ get; set; }
        public UInt32 DataSize{ get; set; }
        public UInt32 Size { get; set; }
        public List<Symbol> Symbols { get; set; }

        public Module()
        {
            new Module("", 0);
        }

        public Module(string mod, UInt32 txt, UInt32 bss, UInt32 data)
        {
            ModuleName = mod;
            TextSize = txt;
            BSSSize = bss;
            DataSize = data;
        }

        public Module(string mod, UInt32 siz)
        {
            ModuleName = mod;
            Size = siz;
            Symbols = new List<Symbol>();
        }
    }
    /// <summary>
    /// Map file generated by --> LDFLAGS += -Wl,--print-map > $(PJ_NAME).map
    /// </summary>
    class MAPParser
    {
        bool DEBUG = false;
        const int C_OFFSET_ADDRESS = 0;
        const int C_OFFSET_SIZE = 1;
        const string C_MEM_MAP_HEADER = "Linker script and memory map";
        const string C_MEM_MAP_END_MARKER = ".stab";
        // Segment to section mapping is done via settings
        public string[] C_TEXT_ID ; //= new string[] { ".text" };
        public string[] C_DATA_ID;// = new string[] { ".data", ".rodata", ".strings", "._pm" };
        public string[] C_BSS_ID;//= new string[] { ".bss", "COMMON" };
        string[] AllSections;
        const int C_MODULE_NAME_CHAR_POS = 38;
        // Note: fill memory is totalled for a section and displayed
        string C_FILL_IDENTIFIER = "*fill*"; // All fill bytes are identified by this string in a Map file

        List<Symbol> TextSegment;
        List<Symbol> BSS;
        List<Symbol> Data;
        public List<Module> ModuleMap;
        public List<Section> Sections;
        // return all the symbols in each section and each module
        public List<Symbol> MapSymbols { get { return Sections.SelectMany(sec => sec.Modules.SelectMany(mod => mod.Symbols.Select(s => s))).ToList(); } }
        public UInt32 TextSegSize = 0; // the size reported by linker
        public UInt32 BssSize = 0;
        public UInt32 DataSize = 0;

        static readonly MAPParser _instance = new MAPParser();

        public static MAPParser Instance
        {
            get { return _instance; }
        }

        public bool Run(string filePath, Action<bool> prog_ind)
        {
            Sections = new List<Section>();
            AllSections = new string[C_TEXT_ID.Length + C_DATA_ID.Length + C_BSS_ID.Length];
            Array.Copy(C_TEXT_ID, 0, AllSections, 0, C_TEXT_ID.Length);
            Array.Copy(C_DATA_ID, 0, AllSections, C_TEXT_ID.Length, C_DATA_ID.Length);
            Array.Copy(C_BSS_ID, 0, AllSections, C_TEXT_ID.Length + C_DATA_ID.Length, C_BSS_ID.Length);

            List<string> Map = File.ReadAllLines(filePath).ToList();

            int MMap_index = Map.FindIndex(x => String.Equals(C_MEM_MAP_HEADER, x));
            int MMap_end = Map.Count; // No need to use an end marker! //Map.FindIndex(x => String.Equals(C_MEM_MAP_END_MARKER, x));  
            if ((MMap_index == -1) || (MMap_end == -1))
            {
                MessageBox.Show("Couldn't find module info in map file! Can't proceed!", "Oops!", MessageBoxButtons.OK); return false;
            }
            Debug.WriteLineIf(DEBUG,"Found Memory map at index :" + MMap_index.ToString());
            TextSegment = new List<Symbol>();
            BSS = new List<Symbol>();
            Data = new List<Symbol>();
            BssSize = FindSegUsePerModule(Map.GetRange(MMap_index, MMap_end - MMap_index), BSS, C_BSS_ID);
            DataSize = FindSegUsePerModule(Map.GetRange(MMap_index, MMap_end - MMap_index), Data, C_DATA_ID);
            TextSegSize = FindSegUsePerModule(Map.GetRange(MMap_index, MMap_end - MMap_index), TextSegment, C_TEXT_ID);
            int sectionIdx = 0, moduleIdx = 0, symbolIdx = 0;
            uint modSymSiz = 0;


            // Sometimes the linker may optimize read only string (?) data
            //.rodata	802CB1	2A	42	D:\Freelance\Study\FTDI\new_build_framework\projects\libihome\Debug/libihome.a(rpc_diskio_write.o)	
            //.rodata	802CB1	1D	29	D:\Freelance\Study\FTDI\new_build_framework\projects\libihome\Debug/libihome.a(rpc_httpc.o)	
            //.rodata	802CB1	2A	42	D:\Freelance\Study\FTDI\new_build_framework\projects\libihome\Debug/libihome.a(rpc_httpc_HttpClient.o)	
            //.rodata	802CB1	121	289	D:\Freelance\Study\FTDI\new_build_framework\projects\libihome\Debug/libihome.a(rpc_httpc_port.o)
            // print out data segment
            Debug.WriteLineIf(DEBUG,"---------------------------");
            long sum = 0;
            for (int i = 0; i + 1 < Data.Count; i++)
            {
                Debug.WriteLineIf(DEBUG,$"{Data[i].SectionName}\t{Data[i].LoadAddress.ToString("X")}\t{Data[i].Size.ToString("X")}\t{Data[i].Size}\t{Data[i].ModuleName}\t");
                sum = sum + Data[i].Size;
                if (sum > (long)(Data[i + 1].LoadAddress & (~SymParser.Instance.RAM_ADDRESS_MASK)))
                    Debug.WriteLineIf(DEBUG,"oops");
            }
            Debug.WriteLineIf(DEBUG, $"{Data.Sum(x => x.Size)}, {sum}");

            bool valid_section = false;
            foreach (string line in Map.GetRange(MMap_index, MMap_end - MMap_index))
            {
                prog_ind?.Invoke(false);

                Section s; bool valid;

                if (line == "") // A blank line indicates a break in the section
                    valid_section = false;

                if (IsSection(line, out valid, out s))
                {
                    valid_section = valid;
                    // Create a new section and update it's size, address and load address if it's available.
                    if (valid)
                    {
                        Sections.Add(s); sectionIdx++; symbolIdx = 0; moduleIdx = 0;
                    }
                    // All subsequent symbols/modules should go into this section until we encounter a new section
                    continue;
                }

                if (!valid_section) continue; // ignore invalid sections

                Module m;
                Symbol sym;
                if (IsModule(line, out m)) // fixme: module line may also be a symbol line eg: .text.ulli2a   0x00000ca0      0x134 lib/tinyprintf/tinyprintf.o
                {
                    // Add the module into the (current) section
                    if (!Sections.LastOrDefault().Modules.Exists(x => x.ModuleName == m.ModuleName))
                    {
                        Sections.LastOrDefault().Modules.Add(m); moduleIdx++; modSymSiz = 0;
                    }
                    else
                    {
                        // Increment the size of the module
                        Sections.LastOrDefault().Modules.Where(x => x.ModuleName == m.ModuleName).First().Size += m.Size;
                    }
                    continue;
                }
                // Add the symbol into the (current) section
                // The symbol size can be calculated by subracting the current address from the last symbol, as the map file is in sorted order
                // fixme: handle FILL symbols, COMMON
                Module mod = Sections.LastOrDefault().Modules.LastOrDefault();
                if (IsGlobalSymbol(line, out sym) && mod != null)
                {
                    sym.SectionName = Sections.LastOrDefault().Name;

                    //if (sym.SymbolName.Contains("*fill*"))
                    //    Debug.Write("oops");
                    sym.ModuleName = mod.ModuleName;
                    sym.GetFileFromModuleName(mod.ModuleName);
                    uint siz = 0;
                    // update the size of the previous symbol, given that we know the address of the current symbol
                    if (mod.Symbols != null && mod.Symbols.Count > 0)
                    {
                        // sum of all sym sizes in a module cant exceed the module size
                        // Size of previous symbol = LA(current) - LA(previous)
                        siz = (uint)(sym.LoadAddress - Sections[sectionIdx - 1].Symbols[symbolIdx - 1].LoadAddress);
                        if (siz + modSymSiz < mod.Size)
                        {
                            Sections[sectionIdx - 1].Symbols[symbolIdx - 1].Size = siz;
                            // FIXME: update module sym size also 
                            mod.Symbols.LastOrDefault().Size = siz;
                            modSymSiz += siz;
                        }
                    }
                    // Temporary size of current symbol = Module Size - Sum(Size of all syms till now)
                    sym.Size = mod.Size - modSymSiz;
                    Sections.LastOrDefault().Symbols.Add(sym);
                    mod.Symbols.Add(sym); symbolIdx++;

                }
                else if (IsFill(line, out sym))
                {
                    Sections.LastOrDefault().FillBytes += sym.Size;
                }
            }

            Debug.WriteLineIf(DEBUG,"Total Text Size: " + TextSegment.Sum(item => item.Size).ToString());

            ModuleMap = new List<Module>();
            // For each entry in the Tmap, find it's module and sum over that module
            foreach (Symbol t in TextSegment)
            {
                if (!ModuleMap.Exists(x => String.Equals(x.ModuleName, t.ModuleName)))
                {
                    AddModule(t);
                }
            }
            foreach (Symbol t in BSS)
            {
                if (!ModuleMap.Exists(x => String.Equals(x.ModuleName, t.ModuleName)))
                {
                    AddModule(t);
                }
            }
            foreach (Symbol t in Data)
            {
                if (!ModuleMap.Exists(x => String.Equals(x.ModuleName, t.ModuleName)))
                {
                    AddModule(t);
                }
            }

            Debug.WriteLineIf(DEBUG, $"Total Text Size (Module Sum): {ModuleMap.Sum(item => item.TextSize)} (Linker): {TextSegSize}");
            Debug.WriteLineIf(DEBUG, $"Total BSS Size (Module Sum): {ModuleMap.Sum(item => item.BSSSize)} (Linker): {BssSize}");
            Debug.WriteLineIf(DEBUG, $"Total DATA Size (Module Sum): {ModuleMap.Sum(item => item.DataSize)} (Linker): {DataSize}");

            // Sanity check
            if ((TextSegSize == ModuleMap.Sum(item => item.TextSize) && TextSegment.Sum(item => item.Size) == TextSegSize) &&
                (BssSize == ModuleMap.Sum(item => item.BSSSize)) &&
                (DataSize == ModuleMap.Sum(item => item.DataSize)))
            {
                Debug.WriteLineIf(DEBUG,"All size calculations match! Sanity check passed!");
                PrintModuleMap();
            }

            prog_ind?.Invoke(true);

            return (true);
        }

        void AddModule(Symbol t)
        {
            Debug.WriteLineIf(DEBUG,"Found a new module : " + t.ModuleName);
            // Find sum of text size of this module
            UInt32 sumt = (UInt32)TextSegment.Where(x => String.Equals(x.ModuleName, t.ModuleName)).Sum(x => x.Size);
            Debug.WriteLineIf(DEBUG,"Module Text Size:" + sumt.ToString());
            UInt32 sumb = (UInt32)BSS.Where(x => String.Equals(x.ModuleName, t.ModuleName)).Sum(x => x.Size);
            Debug.WriteLineIf(DEBUG,"Module BSS Size:" + sumb.ToString());
            UInt32 sumd = (UInt32)Data.Where(x => String.Equals(x.ModuleName, t.ModuleName)).Sum(x => x.Size);
            Debug.WriteLineIf(DEBUG,"Module DATA Size:" + sumd.ToString());
            ModuleMap.Add(new Module(t.ModuleName, sumt, sumb, sumd));
        }

        void PrintModuleMap()
        {
            foreach (Module e in ModuleMap)
            {
                Debug.WriteLineIf(DEBUG,e.TextSize + "\t\t" + e.ModuleName);
            }
        }

        // Since all symbols are sorted in address order, you can calculate the size by subracting the address
        void UpdateSymbolSize()
        {
            foreach (Section s in Sections)
            {
                for (int i = 0; i + 1 < s.Symbols.Count; i++)
                {
                    s.Symbols[i].Size = (uint)(s.Symbols[i + 1].LoadAddress - s.Symbols[i].LoadAddress);
                }
            }
        }

        /// <summary>
        /// Inupt is the whole map file, and the index of the "memory map" section start
        /// </summary>
        /// <param name="Map"></param>
        /// <param name="iMemoryMap"></param>
        UInt32 FindSegUsePerModule(List<string> Map, List<Symbol> seg, string[] segIds)
        {
            UInt32 LinkerRepSize = 0;
            string[] ele;

            foreach (string id in segIds)
            {
                for (int i = 0, j; i < Map.Count; )
                {
                    // Exact match of id with section name
                    if ((Map[i].Split(' ')[0].Trim(new char[] { ' ', '\n', '\r' }).Length == id.Length) && (String.Compare(id, 0, Map[i], 0, id.Length) == 0))
                    {
                        Debug.WriteLineIf(DEBUG,Map[i]);
                        if (Map[i].Split(new char[0], StringSplitOptions.RemoveEmptyEntries).Count() >=2)
                        LinkerRepSize += Convert.ToUInt32(Map[i].Split(new char[0], StringSplitOptions.RemoveEmptyEntries)[2], 16); // this is the linker reported size eg: .text           0x00000000     0x5a48
                        i++;
                        continue;
                    }
                    // Find a line that starts with .text
                    if ((Map[i].Length > id.Length) && (String.Compare(id, 0, Map[i], 1, id.Length) == 0))
                    {
                        //Debug.WriteLineIf(DEBUG,"Found match at : " + i.ToString() + " " + Map[i].ToString());
                        Debug.WriteLineIf(DEBUG,Map[i].ToString());
                        // if the line does not have the load address and other info, look for the next line
                        ele = Map[i].Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                        // Note the path might itself contain spaces, so you have to take all the characters after the last split

                        if (ele.Length >= 4)
                        {
                            string path = Map[i].Substring(Map[i].LastIndexOf(ele[2]) + ele[2].Length).TrimStart(); //Map[i].Substring(C_MODULE_NAME_CHAR_POS); // FIXME: looks like the map file is so generates so that the module path is always at the 38th character. There should be a more portable way to calculate this
                            seg.Add(new Symbol(String.Empty, path, Convert.ToUInt32(ele[1], 16), Convert.ToUInt32(ele[2], 16), id));
                        }
                        else
                        {
                            j = i + 1;
                            if (j == Map.Count) continue;
                            // Check if the next line ends with ".o" or ".o)"
                            if ((Map[j].Length > 2 && String.Compare(".o", 0, Map[j], Map[j].Length - 2, 2) == 0) || (Map[j].Length > 3 && String.Compare(".o)", 0, Map[j], Map[j].Length - 3, 3) == 0))
                            {
                                //Debug.WriteLineIf(DEBUG,"Found sub match at : " + j.ToString() + " " + Map[j].ToString());
                                Debug.WriteLineIf(DEBUG,Map[j].ToString());
                                ele = Map[j].Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                                string path = Map[j].Substring(Map[j].LastIndexOf(ele[1]) + ele[1].Length).TrimStart();//Map[j].Substring(C_MODULE_NAME_CHAR_POS); // FIXME: looks like the map file is so generates so that the module path is always at the 38th character. There should be a more portable way to calculate this
                                seg.Add(new Symbol(String.Empty, path, Convert.ToUInt32(ele[0], 16), Convert.ToUInt32(ele[1], 16), id));
                                i++;
                            }
                        }
                    }
                    i++;
                }
            }

            return LinkerRepSize;
        }


        bool IsSection(string line, out bool valid, out Section s)
        {
            bool ret = false;
            valid = false;
            s = null;
            if (line == "" || line[0] == ' ') return ret;

            // A secton is identified by a pattern of [no-space] [string] [space] [hex-address] [space] hex-size]
            string[] ele = line.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
            if (ele.Length >= 3 && Regex.IsMatch(ele[1], @"0[xX][0-9a-fA-F]+") && Regex.IsMatch(ele[2], @"0[xX][0-9a-fA-F]+")) // Match a Hex number with the first element
            {
                ret = true;
                // Check if it's a section we want to record
                foreach (string id in AllSections)
                {
                    if (line.IndexOf(id) == 0)
                    {
                        valid = true;
                        Debug.WriteLineIf(DEBUG,"Found SECTION " + id + "\n" + line);
                        s = new Section(id, Convert.ToUInt64(ele[1], 16), Convert.ToUInt64(ele[2], 16));
                        break;

                    }
                }
            }
            return ret;
        }


        bool IsSubSection(string line)
        {
            bool ret = false;

            foreach (string id in AllSections)
            {
                if (line.IndexOf(id + ".") == 1) // .text. etc
                {
                    Debug.WriteLineIf(DEBUG,"Found SUB SECTION " + line.Split(new char[0], StringSplitOptions.RemoveEmptyEntries)[0] + "\n" + line);
                    ret = true;
                }
            }

            return ret;
        }


        // A module has a load address field, size field, and an .o file name at the last entry. It also begins with a "[space]section_ID"
        // eg:  .text.myputc   0x00000464       0x18 ./Demo/FT32_GCC/main.o
        // or:  .text.watchdog_handler
        //       0x0000047c       0x10 ./Demo/FT32_GCC/main.o
        bool IsModule(string line, out Module m)
        {
            bool ret = false;
            m = null;

            // if the line does not have the load address and other info, look for the next line
            string[] ele = line.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
            // Note the path might itself contain spaces, so you have to take all the characters after the last split

            if ((ele.Length > 2) &&
                 (String.Compare(".o", 0, line, line.Length - 2, 2) == 0) || (line.Length > 3 && String.Compare(".o)", 0, line, line.Length - 3, 3) == 0))
            {
                string path = "";

                // find size of module - two possibilities
                if (Regex.IsMatch(ele[0], @"0[xX][0-9a-fA-F]+")) // Match a hex number in the format 0xyyyy..
                {
                    // everything after the size is the module path
                    path = line.Substring(line.LastIndexOf(ele[1]) + ele[1].Length).TrimStart();
                    m = new Module(path, Convert.ToUInt32(ele[1], 16));
                    Debug.WriteLineIf(DEBUG,"Found MODULE " + path + "\n" + line);
                    ret = true;
                }
                else if (Regex.IsMatch(ele[1], @"0[xX][0-9a-fA-F]+"))
                {
                    path = line.Substring(line.LastIndexOf(ele[2]) + ele[2].Length).TrimStart();
                    m = new Module(path, Convert.ToUInt32(ele[2], 16));
                    Debug.WriteLineIf(DEBUG,"Found MODULE " + path + "\n" + line);
                    ret = true;
                }

                //if (path != line.Substring(C_MODULE_NAME_CHAR_POS))
                //    Debug.WriteLineIf(DEBUG,"Error in Module Path substring repeats! probably");
            }
            return ret;
        }

        // A symbol contains only a load address and name. Address matches the pattern "0x[number N times]
        bool IsGlobalSymbol(string line, out Symbol sym)
        {
            bool ret = false;
            sym = null;
            string symName = "";

            string[] ele = line.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
            if (ele.Length < 2) return ret;

            symName = String.Join("", ele, 1, ele.Length - 1);
            // Match a Hex number with the first element and make sure the symbol name does not contain invalid C identifier characters
            // The second check is to avoid adding linker script expressions like "_end = ." which appear in the map file into the symbol list
            if (Regex.IsMatch(ele[0], @"0[xX][0-9a-fA-F]+") && !Regex.IsMatch(symName, @"[\s=\+\.\#\(\)]+"))
            {
                Debug.WriteLineIf(DEBUG,"Found SYMBOL " + symName + "\n" + line);
                sym = new Symbol(symName, "", Convert.ToUInt64(ele[0], 16), 0, "");
                ret = true;
            }
            return ret;
        }


        // Fill sections are of the format - *fill*         0x00803425        0x3 
        bool IsFill(string line, out Symbol sym)
        {
            bool ret = false;
            sym = null;

            string[] ele = line.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
            if (ele.Length < 2) return ret;

            if (ele[0].Contains(C_FILL_IDENTIFIER) && Regex.IsMatch(ele[1], @"0[xX][0-9a-fA-F]+"))
            {
                Debug.WriteLineIf(DEBUG,"Found SYMBOL " + C_FILL_IDENTIFIER + "\n" + line);
                sym = new Symbol(C_FILL_IDENTIFIER, "", Convert.ToUInt64(ele[1], 16), (uint)Convert.ToUInt64(ele[2], 16), "");
                ret = true;
            }

            return ret;
        }

    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Memory;


namespace TagStructDumper{
    internal class Program{
        static void Main(string[] args){
            // ARGS:
            // 1 - this should instruct where to offload the plugins to
            // 2 - this should be the current game version, so we know which version of the game we just dumped from
            // 3 - optional tagStructDumper address location, so we could output after an attempt so the loader tool could remember the address and potentially pass it in the next time we run this

            // might also be good to do the module versions

            // EXAMPLE ARGS, and used for testing
            args = new string[] 
            { "C:\\Users\\Joe bingle\\Downloads\\plugins",
              "6.10023.16112.0",
              "1971780059136"
            };


            if (args.Length == 0){ 
                Console.WriteLine("No path arguement detected");
                return;
            }
            if (args.Length == 1){
                Console.WriteLine("No game version arguement detected");
                return;
            }
            // check to make sure that the address is usable
            long start_address = 0;
            if (args.Length > 2){
                if (!string.IsNullOrWhiteSpace(args[2])){
                    try {start_address = Int64.Parse(args[2]);}
                    catch (FormatException){
                        Console.WriteLine($"Failed to read address: '{args[2]}'");
                        return;
                    }
                }
            }

            try
            {
                Console.WriteLine("Arguements accepted, running");
                // if the checks passed, then we probably passed in the correct parameters
                string destination = args[0];
                string game_version = args[1];
                Initializer tag_struct_initer = new Initializer(destination, game_version, start_address);
                if (tag_struct_initer.initialized)
                    Task.Run(() => tag_struct_initer.initTagStructDumper()).Wait();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to initialize tagstruct dumping process");
                Console.WriteLine(ex);
            }

            Console.WriteLine("Process end reached, press enter to exit");
            Console.ReadKey();
        }
    }

    public class Initializer{
        public Initializer(string dest, string vers, long starting_address){
            plugin_output_directory = dest;
            startAddress = starting_address;
            current_game_version = vers;
            // now setup the memory.dll
            M = new Mem();
            if (M.OpenProcess("HaloInfinite.exe")){
                Console.WriteLine("Successfully Hooked to process");
                initialized = true;
            } 
            else Console.WriteLine("No halo infinite process found");
        }
        string plugin_output_directory = "";
        long startAddress = 0;
        string current_game_version = "";
        public bool initialized = false;
        Mem M;
        public async Task initTagStructDumper(){
            // double check to make sure our path and version were specified
            if (string.IsNullOrWhiteSpace(plugin_output_directory)){
                Console.WriteLine("No output path provided");
                return;
            }
            if (string.IsNullOrWhiteSpace(plugin_output_directory)){
                Console.WriteLine("Current game version was not specified (if you do not wish to, provide a non-space character)");
                return;
            }

            // test whether start address was given to us or if not, if we can find it

            if (startAddress != 0){ // we were given an address to start with, lets double check to make sure its right
                long val_test = M.ReadLong((startAddress + 0xC).ToString("X"));
                if (val_test != 7013337615930712659){ // 'SboGgaTa'
                    Console.WriteLine("Provided address was incorrect, reverting to AOB scan");
                    startAddress = 0;
                } // else we're absolutely golden to run this
                else Console.WriteLine("Provided address was correct, skipping AOB scan");
            }

            if (startAddress == 0)
                startAddress = await AoBScan();
            if (startAddress == 0){
                Console.WriteLine("Failed to find starting address");
                return;
            }
            Console.WriteLine("Address Found:" + startAddress.ToString()); // regular decimal so that we can reuse it in this program without converting

            // do Z's thing to count the tags
            int tagCount = 0;
            int warnings = 0;
            long curAddress = startAddress;
            while (true){
                if (M.ReadInt((curAddress + 80).ToString("X")) == 257){
                    tagCount++;
                    curAddress += 88;
                    warnings = 0;
                } else {
                    warnings++;
                    curAddress += 88;
                }
                if (warnings > 3) break; // no idea how exactly this works, but works for me
            }
            Console.WriteLine("Found " + tagCount + " tag structs!");

            // finally, begin the dump
            TagStructDumper tsd = new TagStructDumper(startAddress, tagCount, M, plugin_output_directory);
            if (tsd.DumpStructs(current_game_version)){
                Console.WriteLine("Successfully dumped tag structs");
                return;
            } else {
                Console.WriteLine("Failed to dump tag structs");
                return;
            }
        }
        // heres the AOB scan part, fixed
        private async Task<long> AoBScan(){
            long[] results = (await M.AoBScan("?? ?? ?? ?? ?? ?? ?? ?? ?? ?? ?? ?? 53 62 6F 47 67 61 54 61", true, false)).ToArray();
            if (results.Length == 0)
                return 0;
            return results[0];
        }
    }


    // we do the stuff below

    public class TagStructDumper
    {
        public TagStructDumper(long address, int count, Mem mem, string outdir)
        {
            startAddress = address;
            tagCount = count;
            M = mem;
            outDIR = outdir;
        }

        private XmlWriterSettings xmlWriterSettings = new XmlWriterSettings()
        {
            Indent = true,
            IndentChars = "\t",
        };
        private XmlWriter textWriter;
        private Mem M;
        Regex string_test = new Regex("[^a-zA-Z0-9 !*]$");

        private long startAddress = 0;
        private int tagCount = 0;
        private string outDIR = @".\Plugins";

        public bool DumpStructs(string game_version)
        {
            try
            {
                ClearPlugins();

                for (int iteration_index = 0; iteration_index < tagCount; iteration_index++)
                {
                    // read the crucial infos
                    long current_tag_struct_Address = startAddress + (iteration_index * 88);
                    string group_name = M.ReadString((current_tag_struct_Address + 12).ToString("X"), "", 4);

                    // we now apply the file name before creating it, so no more temp files
                    string outpath = outDIR + @"\" + ReverseString(group_name).Replace("*", "_");
                    // "cmpS" has a similar tag that has the same chars but different capitialization
                    if (File.Exists(outpath + ".xml"))
                        outpath += "NAME_SIMILAR.xml";
                    else
                        outpath += ".xml";

                    using (XmlWriter w = XmlWriter.Create(outpath, xmlWriterSettings))
                    {

                        textWriter = w;
                        textWriter.WriteStartDocument();
                        textWriter.WriteStartElement("root");
                        textWriter.WriteAttributeString("GameVersion", game_version); // notate version, for testing purposes

                        // write the root name
                        string root_name = "";
                        long root_name_address = M.ReadLong((current_tag_struct_Address).ToString("X"));
                        if (root_name_address != 0) root_name = M.ReadString(root_name_address.ToString("X"), "", 300);
                        textWriter.WriteAttributeString("Name", root_name);

                        // write the group parents 
                        string parent_1 = ReverseString(M.ReadString((current_tag_struct_Address + 0x2C).ToString("X"), "", 4));
                        string parent_2 = ReverseString(M.ReadString((current_tag_struct_Address + 0x30).ToString("X"), "", 4));
                        string parent_3 = ReverseString(M.ReadString((current_tag_struct_Address + 0x34).ToString("X"), "", 4));
                        if (!string.IsNullOrWhiteSpace(parent_2)) parent_1 += "," + parent_2;
                        if (!string.IsNullOrWhiteSpace(parent_3)) parent_1 += "," + parent_3;
                        textWriter.WriteAttributeString("Parents", string.Join(",", parent_1));

                        // write the group index
                        textWriter.WriteAttributeString("Index", M.ReadInt((current_tag_struct_Address + 0x28).ToString("X")).ToString());

                        // write the category string
                        long category_name_address = M.ReadLong((current_tag_struct_Address + 0x48).ToString("X"));
                        if (category_name_address != 0) textWriter.WriteAttributeString("C", M.ReadString(category_name_address.ToString("X"), "", 300));

                        // TODO: write data from toplevel group_def_link_struct
                        // we're currently skipping it because it doesn't really contain any useful information

                        // set this up, so we can append our first piece
                        Dictionary<string, long> structs_to_append = new Dictionary<string, long>();
                        // assign our top level struct to the list
                        // this code will convert gdls to the next tagstruct
                        long gdls_address = M.ReadLong((current_tag_struct_Address + 0x20).ToString("X"));
                        long tagstruct_address = M.ReadLong((gdls_address + 0x18).ToString("X"));
                        structs_to_append.Add(read_guid_from_tagstruct(tagstruct_address), tagstruct_address);

                        // loop to write each struct definition
                        // NOTE: this has to point to a 'tagdata_struct_def', refer to the tagstruct mappings file
                        int struct_index = 0;
                        while (structs_to_append.Count > struct_index)
                        {
                            long current_base_address = structs_to_append.ElementAt(struct_index).Value;

                            string GUID = structs_to_append.ElementAt(struct_index).Key;
                            struct_index++;
                            // write guid as node name
                            // this may actually be the incorrect GUID, it could be that one at 0x10
                            int struct_offset = 0;
                            textWriter.WriteStartElement("_" + GUID);
                            //textWriter.WriteAttributeString("GUID_1", read_guid2_from_tagstruct(current_base_address));

                            // read the TWO strings, if they're different then append both
                            string first_name = M.ReadString(M.ReadLong(current_base_address.ToString("X")).ToString("X"), "", 300);
                            textWriter.WriteAttributeString("Name", first_name);
                            string second_name = M.ReadString(M.ReadLong((current_base_address + 8).ToString("X")).ToString("X"), "", 300);
                            if (!string.IsNullOrWhiteSpace(second_name) && second_name != first_name) textWriter.WriteAttributeString("Name2", second_name);

                            // determine the count of children and then process them all
                            int children_count = M.ReadInt((current_base_address + 0x78).ToString("X"));
                            long child_start_address = M.ReadLong((current_base_address + 0x20).ToString("X"));


                            // precount the struct size
                            // wow isn't that handy, we made a function for that
                            // wow isnt that handy, WE HAVE TO CACULATE IT BEFORE READING ANY MF PARAMS
                            // so we just end up running through this whole thing twice, how epic
                            int struct_total_size = test_struct_size(current_base_address);

                            textWriter.WriteAttributeString("Size", struct_total_size.ToString("X"));
                            for (int i = 0; i < children_count; i++)
                            {
                                // figure out the address of this param struct
                                long current_param_address = child_start_address + (i * 0x18);

                                // writenode and name
                                int param_group = M.ReadInt((current_param_address + 0x08).ToString("X"));
                                if (param_group != 0x3B) // this is the end of struct, we dont write this one
                                {
                                    textWriter.WriteStartElement("_" + param_group.ToString("X"));
                                    string DELETEME_name = M.ReadString(M.ReadLong(current_param_address.ToString("X")).ToString("X"), "", 300);
                                    textWriter.WriteAttributeString("Name", DELETEME_name);
                                    // write the signature if it has one
                                    string param_signature = M.ReadString((current_param_address + 0x0C).ToString("X"), "", 4);
                                    if (!string.IsNullOrWhiteSpace(param_signature)) textWriter.WriteAttributeString("type", ReverseString(param_signature));
                                    // write the offset, then add this to the offset size tracker
                                    textWriter.WriteAttributeString("Offset", struct_offset.ToString("X"));
                                    // lets first get the address of the extra data address
                                    // NOTE: not always an address, can be a regular number depending on the group context
                                    long param_data_address = M.ReadLong((current_param_address + 0x10).ToString("X"));
                                    // now for the fat thingo
                                    switch (param_group)
                                    {
                                        // ODD GROUP
                                        //case 0x2:
                                        //	//exe_pointer = M.ReadLong(next_next_next_address.ToString("X"))
                                        //	break;
                                        // CUSTOM BLOCK THINGS
                                        // take no action because the function is missing, thus no data to read
                                        //case 0x2D:
                                        //case 0x2F:
                                        //case 0x31:
                                        //	// actual_value = next_next_next_address;
                                        //	break;
                                        // EXPLANATION
                                        //case 0x36:
                                        //	// we forgot to go back and map this one
                                        //	// you know what, never mind, there is only a single pointer that any of these use
                                        //	// and it points to a blank string
                                        //	break;

                                        // FLAG GROUPS
                                        case 0xA:
                                        case 0xB:
                                        case 0xC:
                                        case 0xD:
                                        case 0xE:
                                        case 0xF:
                                            if (param_data_address > 0)
                                            {
                                                textWriter.WriteAttributeString("StructName1", M.ReadString(M.ReadLong((param_data_address).ToString("X")).ToString("X"), "", 300));
                                                int count_of_children = M.ReadInt((param_data_address + 8).ToString("X"));
                                                long children_address = M.ReadLong((param_data_address + 16).ToString("X"));
                                                for (int f_i = 0; f_i < count_of_children; f_i++)
                                                {
                                                    textWriter.WriteStartElement("Flag");
                                                    textWriter.WriteAttributeString("n", M.ReadString(M.ReadLong((children_address + (f_i * 8)).ToString("X")).ToString("X"), "", 300));
                                                    textWriter.WriteEndElement();
                                                }
                                            }
                                            break;
                                        // BLOCK THINGS 'Group_def_link_struct'
                                        case 0x29:
                                        case 0x2A:
                                        case 0x2B:
                                        case 0x2C:
                                        case 0x2E:
                                        case 0x30:
                                        case 0x40: // actual tagblock
                                            if (param_data_address > 0)
                                            {
                                                long block_tagstruct_address = M.ReadLong((param_data_address + 0x18).ToString("X"));
                                                string target_guid = read_guid_from_tagstruct(block_tagstruct_address);
                                                // write the target guid to the node
                                                textWriter.WriteAttributeString("GUID", target_guid.ToString());
                                                //textWriter.WriteAttributeString("GUID_1", read_guid2_from_tagstruct(block_tagstruct_address));
                                                // write the struct names
                                                string first_block_name = M.ReadString(M.ReadLong(param_data_address.ToString("X")).ToString("X"), "", 300);
                                                textWriter.WriteAttributeString("StructName1", first_name);
                                                string second_block_name = M.ReadString(M.ReadLong((param_data_address + 8).ToString("X")).ToString("X"), "", 300);
                                                if (!string.IsNullOrWhiteSpace(second_name) && second_name != first_name) textWriter.WriteAttributeString("StructName2", second_name);
                                                // read min and max values
                                                textWriter.WriteAttributeString("Min", M.ReadInt((param_data_address + 0x10).ToString("X")).ToString());
                                                textWriter.WriteAttributeString("Max", M.ReadInt((param_data_address + 0x14).ToString("X")).ToString());
                                                // double check to make sure it isn't referencing one we already added
                                                if (!structs_to_append.Keys.Contains(target_guid)) structs_to_append.Add(target_guid, block_tagstruct_address);
                                            }
                                            break;
                                        // PADDING BLOCKS
                                        case 0x34:
                                        case 0x35:
                                            textWriter.WriteAttributeString("Length", param_data_address.ToString());
                                            //struct_offset += (int) param_data_address; // since they have a custom length, we have to manually increase the size tracker
                                            // this functionality is provided in the test size function
                                            break;
                                        // CUSTOM - four floats (sometimes)
                                        case 0x37:
                                            if (param_data_address > 0)
                                            {
                                                textWriter.WriteAttributeString("Float1", M.ReadFloat((param_data_address + 0x0).ToString("X")).ToString());
                                                textWriter.WriteAttributeString("Min", M.ReadFloat((param_data_address + 0x4).ToString("X")).ToString());
                                                textWriter.WriteAttributeString("Max", M.ReadFloat((param_data_address + 0x8).ToString("X")).ToString());
                                                textWriter.WriteAttributeString("Float4", M.ReadFloat((param_data_address + 0xC).ToString("X")).ToString());
                                            }
                                            break;
                                        // STRUCT STRUCT
                                        case 0x38:
                                            if (param_data_address > 0)
                                            {
                                                // read guid, then add it to the dict
                                                string struct_guid = read_guid_from_tagstruct(param_data_address);
                                                textWriter.WriteAttributeString("GUID", struct_guid);
                                                //textWriter.WriteAttributeString("GUID_1", read_guid2_from_tagstruct(param_data_address));
                                                if (!structs_to_append.Keys.Contains(struct_guid))
                                                    structs_to_append.Add(struct_guid, param_data_address);
                                            }
                                            break;
                                        // ARRAY STRUCT
                                        case 0x39:
                                            if (param_data_address > 0)
                                            {
                                                // read guid, then add it to the dict
                                                long array_struct_address = M.ReadLong((param_data_address + 0x18).ToString("X"));
                                                string struct_guid = read_guid_from_tagstruct(array_struct_address);
                                                textWriter.WriteAttributeString("GUID", struct_guid);
                                                //textWriter.WriteAttributeString("GUID_1", read_guid2_from_tagstruct(array_struct_address));
                                                if (!structs_to_append.Keys.Contains(struct_guid))
                                                    structs_to_append.Add(struct_guid, array_struct_address);
                                                // also append array count and array string
                                                textWriter.WriteAttributeString("Count", M.ReadInt((param_data_address + 0x08).ToString("X")).ToString());
                                                textWriter.WriteAttributeString("StructName1", M.ReadString(M.ReadLong(param_data_address.ToString("X")).ToString("X"), "", 300));

                                                // test if exists
                                                long Unk_0x10 = M.ReadLong((param_data_address + 0x10).ToString("X"));
                                                if (Unk_0x10 != 0)
                                                { // unknown val

                                                }
                                            }
                                            break;
                                        case 0x41:
                                            if (param_data_address > 0)
                                            { // requires further investigation

                                                int ref_Flags = M.ReadInt(param_data_address.ToString("X")); // not quite figured these out yet
                                                string allowed_group = M.ReadString((param_data_address + 4).ToString("X"), "", 4);
                                                long extra_tags_stuct = M.ReadLong((param_data_address + 8).ToString("X"));
                                                string allowed_groups = "";
                                                if (extra_tags_stuct > 0)
                                                {
                                                    // apparently the groups are just alligned and should be read as if they were a really large null terminated string
                                                    // so we loop until we read a '-1'
                                                    int allowed_group_index = 0;
                                                    while (true)
                                                    {
                                                        byte[] next_group = M.ReadBytes((extra_tags_stuct + allowed_group_index).ToString("X"), 4);
                                                        int invalid_check = BitConverter.ToInt32(next_group, 0);
                                                        if (invalid_check == -1 || invalid_check == 0) break;

                                                        if (allowed_group_index > 0)
                                                            allowed_groups += ", ";
                                                        allowed_groups += ReverseString(System.Text.Encoding.UTF8.GetString(next_group, 0, 4));
                                                        allowed_group_index += 4;
                                                    }
                                                } // bad way to check to be honest, this may cause complications in the future with special tags idk
                                                else if (!string_test.IsMatch(allowed_group))
                                                    allowed_groups = ReverseString(allowed_group);

                                                textWriter.WriteAttributeString("Allowed", allowed_groups);
                                                textWriter.WriteAttributeString("Flags", ref_Flags.ToString());
                                            }
                                            break;
                                        case 0x42: // incomplete mapping
                                            if (param_data_address > 0)
                                            {
                                                textWriter.WriteAttributeString("StructName1", M.ReadString(M.ReadLong(param_data_address.ToString("X")).ToString("X"), "", 300));
                                                textWriter.WriteAttributeString("Int1", M.ReadInt((param_data_address + 8).ToString("X")).ToString());
                                                textWriter.WriteAttributeString("Int2", M.ReadInt((param_data_address + 0xC).ToString("X")).ToString());
                                                textWriter.WriteAttributeString("Flags", M.ReadInt((param_data_address + 0x10).ToString("X")).ToString("X"));
                                                long function_string_ptr = M.ReadLong((param_data_address + 0x18).ToString("X"));
                                                if (function_string_ptr > 0) textWriter.WriteAttributeString("Function", M.ReadString(function_string_ptr.ToString("X"), "", 300));

                                                // test if these types are ever used
                                                long Unk_0x14 = M.ReadInt((param_data_address + 0x14).ToString("X"));
                                                long Unk_0x20 = M.ReadLong((param_data_address + 0x20).ToString("X"));
                                                long Unk_0x28 = M.ReadLong((param_data_address + 0x28).ToString("X"));
                                                long Unk_0x30 = M.ReadLong((param_data_address + 0x30).ToString("X"));
                                                if (Unk_0x14 != 0 || Unk_0x20 != 0 || Unk_0x28 != 0 || Unk_0x30 != 0)
                                                {
                                                    // ok we just found one that has something that we haven't seen yet
                                                }
                                            }
                                            break;
                                        case 0x43:
                                            if (param_data_address > 0)
                                            {
                                                textWriter.WriteAttributeString("StructName1", M.ReadString(M.ReadLong(param_data_address.ToString("X")).ToString("X"), "", 300));
                                                textWriter.WriteAttributeString("Int1", M.ReadLong((param_data_address + 8).ToString("X")).ToString());
                                                long struct_params = M.ReadLong((param_data_address + 0x10).ToString("X"));
                                                if (struct_params > 0)
                                                {
                                                    // read guid, then add it to the dict
                                                    string struct_guid = read_guid_from_tagstruct(struct_params);
                                                    textWriter.WriteAttributeString("GUID", struct_guid);
                                                    //textWriter.WriteAttributeString("GUID_1", read_guid2_from_tagstruct(struct_params));
                                                    if (!structs_to_append.Keys.Contains(struct_guid))
                                                        structs_to_append.Add(struct_guid, struct_params);
                                                }
                                            }
                                            break;
                                        case 0x44:
                                            if (param_data_address != 0) // none seemed to have an address, but leaving this code as a reminder to test it again 10 years from now 
                                            {

                                            }
                                            break;
                                        case 0x45: // has a tag group 4 chars "Kit!" 
                                            if (param_data_address != 0)
                                            {
                                                // NOTE: we have to re-read the param_data_address to interpret it as a string
                                                string allowed_group = M.ReadString((current_param_address + 0x10).ToString("X"), "", 4);
                                                if (!string_test.IsMatch(allowed_group))
                                                    textWriter.WriteAttributeString("Group", ReverseString(allowed_group));
                                            }
                                            break;
                                    }
                                    // use our epic function to figure out how many bytes this thing takes up, especially if its a struct
                                    // so our next value knows exactly the offset that they are at
                                    struct_offset += test_struct_param_size(current_param_address);

                                    // close off the param element
                                    textWriter.WriteEndElement();
                                }

                            }
                            // close off the struct element
                            textWriter.WriteEndElement();
                        }

                        // finally, close off the root node, and close the file

                        textWriter.WriteEndElement();
                        textWriter.WriteEndDocument();
                        textWriter.Close();
                        // onto the next tag
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return false;
            }
        }

        public string read_guid_from_tagstruct(long tagstruct_address)
        {
            // we need to read this as two longs, then reverse both of them and add them together
            // although we shoudln't need to read the 
            string guid = M.ReadLong((tagstruct_address + 0x10).ToString("X")).ToString("X8");
            guid += M.ReadLong((tagstruct_address + 0x18).ToString("X")).ToString("X8");
            return guid;
        }
        // no longer need a second function as we're only reading the actual guid
        // not sure the the other was, potentially a hash?
        //public string read_guid2_from_tagstruct(long tagstruct_address)
        //{ 
        //	return Convert.ToHexString(M.ReadBytes((tagstruct_address + 0x10).ToString("X"), 16)); 
        //}

        private static string ReverseString(string myStr)
        {
            char[] myArr = myStr.ToCharArray();
            Array.Reverse(myArr);
            return new string(myArr);
        }

        private void ClearPlugins()
        {
            foreach (string file in Directory.EnumerateFiles(outDIR))
            {
                File.Delete(file);
            }
        }
        List<int> tested_groups = new List<int>();

        int test_struct_size(long struct_ptr)
        {
            long struct_params_address = M.ReadLong((struct_ptr + 0x20).ToString("X"));
            int children_count = M.ReadInt((struct_ptr + 0x78).ToString("X"));

            int output_size = 0;
            for (int i = 0; i < children_count; i++)
            {
                output_size += test_struct_param_size(struct_params_address + (i * 0x18));
            }
            return output_size;
        }
        public int test_struct_param_size(long current_param_address)
        {
            int output_size = 0;
            int param_group = M.ReadInt((current_param_address + 0x08).ToString("X"));
            // NOTE: this will be read as a regular long for the padding blocks
            long param_data_address = M.ReadLong((current_param_address + 0x10).ToString("X"));

            output_size += param_group_sizes[param_group];
            // then we consider inlined structs and padding blocks
            switch (param_group)
            {
                // BLOCK THINGS 'Group_def_link_struct'
                // these are not inlined as far as im aware
                //case 0x29:
                //case 0x2A:
                //case 0x2B:
                //case 0x2C:
                //case 0x2E:
                //case 0x30:
                //case 0x40: // actual tagblock
                //	break;
                // RESOURCE BLOCK - this is not inlined probably
                //case 0x43:
                //	break;

                // PADDING BLOCKS - these have dynamic lengths that we need to account
                case 0x34:
                case 0x35:
                    output_size += (int)param_data_address;
                    break;
                // STRUCT STRUCT - child_struct_size
                case 0x38:
                    // ok now time to get serious, we have to read all the contents of this struct, and then any children structs
                    // ideally we wouldn't use a recursive function, but i mean whatever, today is your lucky day mr recursion
                    output_size += test_struct_size(param_data_address);
                    break;
                // ARRAY STRUCT - child_struct_size * count *
                // may or may not be inlined
                case 0x39:
                    long array_struct_address = M.ReadLong((param_data_address + 0x18).ToString("X"));
                    // poor man's for loop
                    long array_length = M.ReadLong((param_data_address + 0x08).ToString("X"));
                    while (array_length > 0)
                    {
                        array_length--;
                        output_size += test_struct_size(array_struct_address);
                    }
                    break;
            }
            return output_size;
        }

        // these should all be correct except for the unknown/unlabelled ones
        public static int[] param_group_sizes = new int[]
        {
            32,     // _field_string
            256,    // _field_long_string
            4,      // _field_string_id
            4,      // 
            1,      // _field_char_integer
            2,      // _field_short_integer
            4,      // _field_long_integer
            8,      // _field_int64_integer
            4,      // _field_angle
            4,      // _field_tag
            1,      // _field_char_enum
            2,      // _field_short_enum
            4,      // _field_long_enum
            4,      // _field_long_flags
            2,      // _field_word_flags
            1,      // _field_byte_flags
            4,      // _field_point_2d
            4,      // _field_rectangle_2d
            4,      // _field_rgb_color
            4,      // _field_argb_color 
            4,      // _field_real
            4,      // _field_real_fraction
            8,      // _field_real_point_2d
            12,     // _field_real_point_3d
            8,      // _field_real_vector_2d
            12,     // _field_real_vector_3d
            16,     // _field_real_quaternion
            8,      // _field_real_euler_angles_2d
            12,     // _field_real_euler_angles_3d
            12,     // _field_real_plane_2d
            16,     // _field_real_plane_3d
            12,     // _field_real_rgb_color
            16,     // _field_real_argb_color
            4,      // _field_real_hsv_colo
            4,      // _field_real_ahsv_color
            4,      // _field_short_bounds
            8,      // _field_angle_bounds
            8,      // _field_real_bounds
            8,      // _field_real_fraction_bounds
            4,      // 
            4,      //
            4,      // _field_long_block_flags
            4,      // _field_word_block_flags
            4,      // _field_byte_block_flags
            1,      // _field_char_block_index
            1,      // _field_custom_char_block_index
            2,      // _field_short_block_index
            2,      // _field_custom_short_block_index
            4,      // _field_long_block_index
            4,      // _field_custom_long_block_index
            4,      // 
            4,      // 
            0,      // _field_pad 
            0,      // _field_skip
            0,      // _field_explanation
            0,      // _field_custom
            0,      // _field_struct 
            0,      // _field_array
            4,      // 
            0,      // end of struct
            1,      // _field_byte_integer
            2,      // _field_word_integer
            4,      // _field_dword_integer
            8,      // _field_qword_integer
            20,     // _field_block_v2
            28,     // _field_reference_v2
            24,     // _field_data_v2
            16,     // tag_resource
            4,      // UNKNOWN
            4       // UNKNOWN
        };
    }
}

using CommandLine;

namespace PacsGrab
{
    public class Options
    {
        [Option('c',"csv",Required = true,HelpText = "CSV file to read IDs from")]
        public string CsvFile { get; set; }
        
        [Option('h',"pacshost",Required = true,HelpText = "PACS Host name")]
        public string PacsHost { get; set; }
        
        [Option('l', "listenport", Required = false, HelpText = "TCP port to listen for incoming connections")]
        public int ListenPort { get; set; } = 104;

        [Option('n',"pacsname",Required = false,HelpText = "PACS AE Title (name)")]
        public string PacsName { get; set; }

        [Option('o',"outputdir",Required=false,HelpText = "Output directory")]
        public string Outpath { get; set; } = ".";

        [Option('p', "pacsport", Required = false, HelpText = "PACS TCP port number")]
        public int PacsPort { get; set; } = 104;

        [Option('s',longName:"selfname",Required = true,HelpText = "Name to use for ourself")]
        public string SelfName { get; set; }
    }
}
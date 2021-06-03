using System;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.Design;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using CsvHelper;
using Dicom;
using Dicom.Log;
using Dicom.Network;
using Dicom.Network.Client;
using DicomClient = Dicom.Network.Client.DicomClient;

namespace PacsGrab
{
    class Program
    {
        /// <summary>
        /// Two stages of exit: first ctrl-C = exit soon
        /// (finish current patient then quit), second = abort
        /// </summary>
        private static bool _politeExit = false;
        private static CancellationTokenSource _cts;

        private static async Task _main(Options o)
        {
            var studyToPatient=new ConcurrentDictionary<string, string>();
            using var reader = new StreamReader(o.CsvFile);
            using var csv = new CsvReader(reader,CultureInfo.CurrentCulture);
            await csv.ReadAsync();
            csv.ReadHeader();

            using var s = Storer.NewStorer(o.Outpath, o.ListenPort, studyToPatient);
            await s.StartAsync("0.0.0.0",o.ListenPort,null,Encoding.Default,new DicomServiceOptions(),null);
            var client = new DicomClient(o.PacsHost, o.PacsPort, false, o.SelfName, o.PacsName);
            client.NegotiateAsyncOps();
            while (!_politeExit && !_cts.IsCancellationRequested && await csv.ReadAsync())
            {
                var patientId = csv.GetField("ID");
                var q = query(patientId, csv.GetField("Dates"));
                q.OnResponseReceived += async (req, resp) =>
                {
                    if (resp.Status == DicomStatus.Pending)
                    {
                        var id = resp.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, String.Empty);
                        if (!String.IsNullOrWhiteSpace(id))
                        {
                            studyToPatient.TryAdd(id, patientId);
                            await client.AddRequestAsync(fetch(id));
                            await client.SendAsync(_cts.Token,DicomClientCancellationMode.ImmediatelyReleaseAssociation);
                        }
                    }
                };
                await client.AddRequestAsync(q);
                await client.SendAsync(_cts.Token,DicomClientCancellationMode.ImmediatelyReleaseAssociation);
            }
            s.Stop();
        }

        private static DicomCMoveRequest fetch(string id)
        {
            return null;
        }

        private static DicomCFindRequest query(string id, string dates)
        {
            var r = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
            r.Dataset.AddOrUpdate(new DicomTag(8, 5), "ISO_IR 100");
            r.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, "");
            
            // TODO: Filter on this later?
            r.Dataset.AddOrUpdate(DicomTag.ModalitiesInStudy, "");

            // Search criteria: patientid given, in date range
            r.Dataset.AddOrUpdate(DicomTag.PatientID, id);
            r.Dataset.AddOrUpdate(DicomTag.StudyDate, dates);
            return r;
        }
        
        static void Main(string[] args)
        {
            _cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, args) =>
            {
                var exitType=_politeExit?"urgently":"after the current patient";
                if (_politeExit)
                    _cts.Cancel();
                Console.Error.WriteLine($"Cancel received, exiting {exitType}");
                _politeExit = true;
                args.Cancel = false;
            };
            Parser.Default.ParseArguments<Options>(args).WithParsedAsync(_main);
        }
    }
}

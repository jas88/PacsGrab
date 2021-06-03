using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Dicom;
using Dicom.IO;
using Dicom.IO.Writer;
using Dicom.Log;
using Dicom.Network;

namespace PacsGrab
{
    public class Storer : DicomService, IDicomServiceProvider, IDicomCStoreProvider, IDicomCEchoProvider
    {
        private static ConcurrentDictionary<string, string> _studyToPatient;
        private static string _outpath;
        private static string _aename = null;
        private readonly DicomTransferSyntax[] _syntaxes;

        public static IDicomServer NewStorer(string outpath, int port,
            ConcurrentDictionary<string, string> studyToPatient)
        {
            _outpath = outpath;
            _studyToPatient = studyToPatient;
            return DicomServer.Create<Storer>(104);
        }
        
        private Storer(INetworkStream stream,Encoding enc,Logger log) : base(stream,enc,log)
        {
            _syntaxes = typeof(DicomTransferSyntax).GetFields(BindingFlags.Static)
                .Where(t => t.FieldType == typeof(DicomTransferSyntax)).Select(t=>(DicomTransferSyntax)(t.GetValue(null))).ToArray();
        }

        protected override void CreateCStoreReceiveStream(DicomFile newfile)
        {
            base._dimseStream?.DisposeAsync();
            
            var studyUid = newfile.FileMetaInfo.MediaStorageSOPClassUID.ToString();
            var instanceUid = newfile.FileMetaInfo.MediaStorageSOPInstanceUID.ToString();
            if (!_studyToPatient.TryGetValue(studyUid, out var patientId))
            {
                Console.Error.WriteLine($"WARNING:Unexpected study {studyUid} for unknown patient");
                patientId = "unknown";
            }
            string outDir = Path.Combine(_outpath, patientId);
            if (!Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);
            base._dimseStreamFile =
                new DesktopFileReference(Path.Combine(outDir,
                    instanceUid));
            base._dimseStream = new BufferedStream(base._dimseStreamFile.Create());
            newfile.Save(base._dimseStream);
            base._dimseStream.Seek(0, SeekOrigin.End);
        }
        
        public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
        {
            if (_aename!=null && _aename!=association.CalledAE)
            {
                return SendAssociationRejectAsync(
                    DicomRejectResult.Permanent,
                    DicomRejectSource.ServiceUser,
                    DicomRejectReason.CalledAENotRecognized);
            }

            foreach (var pc in association.PresentationContexts)
            {
                if (pc.AbstractSyntax == DicomUID.Verification) pc.AcceptTransferSyntaxes(_syntaxes);
                else if (pc.AbstractSyntax.StorageCategory != DicomStorageCategory.None) pc.AcceptTransferSyntaxes(_syntaxes);
            }

            return SendAssociationAcceptAsync(association);
        }
        
        public DicomCEchoResponse OnCEchoRequest(DicomCEchoRequest request)
        {
            return new DicomCEchoResponse(request, DicomStatus.Success);
        }

        public DicomCStoreResponse OnCStoreRequest(DicomCStoreRequest request)
        {
            // Just return success: we already stored the whole file in CreateCStoreReceiveStream above
            return new DicomCStoreResponse(request, DicomStatus.Success);
        }

        public void OnCStoreRequestException(string filename, Exception e)
        {
            if (filename != null)
            {
                File.Delete(filename);
            }
            Console.WriteLine($"Exception '{e}' handling CStore into file '{filename}'");
        }

        public Task OnReceiveAssociationReleaseRequestAsync()
        {
            return SendAssociationReleaseResponseAsync();
        }

        public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason)
        {
            Console.WriteLine($"Abort received");
        }

        public void OnConnectionClosed(Exception exception)
        {
            if (exception != null)
            {
                Console.WriteLine($"Connection closed with exception '{exception}'");
            }
        }
        
    }
}

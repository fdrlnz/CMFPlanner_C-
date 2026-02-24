using CMFPlanner.Core.Models;

namespace CMFPlanner.Core.Session;

public interface ISessionState
{
    DicomVolume? DicomVolume { get; set; }
    VolumeData?  VolumeData  { get; set; }
    bool HasDicomData  { get; }
    bool HasVolumeData { get; }
}

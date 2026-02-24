using CMFPlanner.Core.Models;

namespace CMFPlanner.Core.Session;

public sealed class SessionState : ISessionState
{
    public DicomVolume? DicomVolume { get; set; }
    public VolumeData?  VolumeData  { get; set; }
    public bool HasDicomData  => DicomVolume is not null;
    public bool HasVolumeData => VolumeData  is not null;
}

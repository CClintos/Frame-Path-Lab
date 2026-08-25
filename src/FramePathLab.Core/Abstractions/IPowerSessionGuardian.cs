namespace FramePathLab.Core.Abstractions;

public interface IPowerSessionGuardian
{
    void Arm(Guid sessionId, Guid nonce, int ownerProcessId);

    bool IsArmed(Guid sessionId);
}

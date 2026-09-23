namespace HouseFlow.Application.Common;

/// <summary>
/// Thrown when accepting an invitation keeps losing the Postgres serializable-transaction
/// race (SQLSTATE 40001) after every retry. The caller should ask the user to try again.
/// </summary>
public class InvitationAcceptConflictException : Exception
{
    public InvitationAcceptConflictException()
        : base("Could not accept this invitation due to a concurrent update. Please try again.")
    {
    }
}

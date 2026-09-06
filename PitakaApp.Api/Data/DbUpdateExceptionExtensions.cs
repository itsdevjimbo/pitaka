using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace PitakaApp.Api.Data;

public static class DbUpdateExceptionExtensions
{
    // MySQL error number for a duplicate entry on a unique index. The store's own
    // uniqueness check is the common path everywhere this matters; this is the backstop
    // for the instant where two writers both pass that check and race to the index.
    private const int DuplicateKeyErrorNumber = 1062;

    // True when the failed save is a unique-index collision rather than some other
    // database fault — the caller translates that to its own "already taken" outcome
    // and lets anything else propagate. Shared so RegisterUser and RedeemEmailChange
    // do not each carry the raw MySqlException.Number.
    public static bool IsUniqueConstraintViolation(this DbUpdateException exception) =>
        exception.InnerException is MySqlException { Number: DuplicateKeyErrorNumber };
}

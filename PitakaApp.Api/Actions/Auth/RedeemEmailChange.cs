using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public enum RedeemEmailChangeOutcome
{
    Succeeded,

    // Unknown Profile, bad token, expired token, address no longer pending — every
    // non-uniqueness failure collapses here, where it is detected, into the one
    // indistinguishable outcome the controller renders as a single non-specific 400.
    // Same shape as ConfirmEmail / ResetPassword.
    // Same collapsed outcome ConfirmEmail and ResetPassword express as a bare bool —
    // here an enum only because the uniqueness failure below needs a third case.
    Invalid,

    // The address was claimed by another Profile between the request and this
    // redemption. Its own outcome, and its own 409, so the person knows to ask again
    // with a different address rather than retry a link that can never work.
    EmailTaken,
}

// The second half of the change-email flow (ADR 0014). Redeeming a valid link moves the
// Profile to its pending address: Email is set, UserName is re-mirrored onto it, and the
// pending columns are cleared — all in one transaction, because Identity's own
// primitive does not touch UserName and in this repo an Email and a UserName that
// disagree is a person who cannot sign in.
public class RedeemEmailChange(
    UserManager<User> userManager,
    PitakaDbContext context,
    TimeProvider timeProvider
)
{
    // The store's own uniqueness check catches an address already held; a Profile that
    // claims it after that check and before this redemption reaches the index is the
    // narrow race left. It surfaces as either an Identity DuplicateUserName/DuplicateEmail
    // result or a unique-constraint DbUpdateException, and both routes answer 409.
    private static readonly string[] DuplicateCodes = ["DuplicateUserName", "DuplicateEmail"];

    private readonly UserManager<User> _userManager = userManager;
    private readonly PitakaDbContext _context = context;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<RedeemEmailChangeOutcome> ExecuteAsync(RedeemEmailChangeInput input)
    {
        var user = await _userManager.FindByIdAsync(input.UserId.ToString());
        if (user is null)
        {
            return RedeemEmailChangeOutcome.Invalid;
        }

        // A pending address whose expiry has passed is treated as absent (ADR 0014): its
        // link does not redeem. Collapsed into the same Invalid outcome as a bad token —
        // an onlooker cannot tell an expired change from a forged one. The stored expiry
        // and the token lifespan come from the one EmailChangeOption value, so a token
        // that survives this check has not expired either. PendingEmailAsOf is the
        // shared "treated as absent" rule, the same one the Profile read applies.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (user.PendingEmailAsOf(now) is not { } pendingEmail)
        {
            return RedeemEmailChangeOutcome.Invalid;
        }

        // ChangeEmailAsync verifies the token against the purpose string it re-derives
        // from the address passed here — the *stored* pending address, never one taken
        // from the request. A superseded request overwrote PendingEmail, so an earlier
        // link's token no longer matches and fails here as an ordinary bad token: that
        // is what makes the old link genuinely dead rather than merely stale (spec
        // stories 7 and 13, decision 18).
        //
        // The Email write, the UserName re-mirror and the pending clear commit together.
        // ChangeEmailAsync SaveChanges-es on its own and Microsoft's reference UI syncs
        // UserName in a second, non-atomic call; a failure between the two would leave a
        // Profile whose login name no longer matches its address.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var changed = await _userManager.ChangeEmailAsync(user, pendingEmail, input.Token);
            if (!changed.Succeeded)
            {
                // ChangeEmailAsync runs the uniqueness validator (RequireUniqueEmail):
                // an address taken by another Profile since the request comes back as a
                // duplicate code, the one redemption failure with its own status. Every
                // other failure — bad token, expired token — is Invalid.
                return changed.Errors.Any(e => DuplicateCodes.Contains(e.Code))
                    ? RedeemEmailChangeOutcome.EmailTaken
                    : RedeemEmailChangeOutcome.Invalid;
            }

            // ChangeEmailAsync has already set EmailConfirmed = true as part of
            // redemption. That is a second code path to that flag besides confirm-email;
            // it is a no-op here, because only a signed-in — therefore already confirmed
            // — Profile can have a pending email to redeem. Noted so a reader does not
            // hunt for where else the flag is set (spec decision 20 / ADR 0014).

            user.UserName = pendingEmail;
            user.PendingEmail = null;
            user.PendingEmailExpiresAt = null;

            // UpdateAsync re-normalizes UserName for its lookup column. If the winning
            // Profile committed the address on its UserName leg between ChangeEmailAsync
            // above and here, this comes back as a duplicate code — the same 409 as the
            // email leg. Anything else leaves the transaction uncommitted and the Email
            // write above rolls back with it.
            var updated = await _userManager.UpdateAsync(user);
            if (!updated.Succeeded)
            {
                return updated.Errors.Any(e => DuplicateCodes.Contains(e.Code))
                    ? RedeemEmailChangeOutcome.EmailTaken
                    : RedeemEmailChangeOutcome.Invalid;
            }

            await transaction.CommitAsync();
            return RedeemEmailChangeOutcome.Succeeded;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // The narrow race the validator cannot catch: the address was free when
            // ChangeEmailAsync looked, and another Profile's redemption committed before
            // this one reached the unique index. Same 409 as the validator-detected case
            // — uniqueness is checked twice and the database is the backstop.
            return RedeemEmailChangeOutcome.EmailTaken;
        }
    }
}

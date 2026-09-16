/**
 * Reads the "organization_id" claim straight out of the JWT payload —
 * base64url-decoded, never signature-verified client-side (there is no way
 * to verify an HS256 signature without the shared secret, and there's no
 * need to: the server re-validates every request against the real signing
 * key via Program.cs's TokenValidationParameters regardless of what the
 * frontend trusts).
 *
 * This exists because AuthResponseDto (see JwtTokenGenerator/AuthController)
 * returns token/userId/fullName/email/role/permissions but *not*
 * organizationId, even though JwtTokenGenerator already stamps
 * "organization_id" onto every token it issues. Rather than change the
 * backend contract for a frontend-only Del, the frontend reads the claim
 * that's already there — it needs an organization id to default
 * CreateTicketRequest.organizationId to the caller's own organization
 * without asking every Customer/Agent to go find their org's GUID by hand
 * (most roles that can create a ticket don't even hold Organization.Read).
 */
export function readOrganizationIdFromToken(token: string): string | null {
  try {
    const payloadSegment = token.split(".")[1];
    if (!payloadSegment) return null;
    const base64 = payloadSegment.replace(/-/g, "+").replace(/_/g, "/");
    const json = decodeURIComponent(
      atob(base64)
        .split("")
        .map((c) => "%" + c.charCodeAt(0).toString(16).padStart(2, "0"))
        .join(""),
    );
    const payload = JSON.parse(json) as Record<string, unknown>;
    const value = payload["organization_id"];
    return typeof value === "string" ? value : null;
  } catch {
    return null;
  }
}

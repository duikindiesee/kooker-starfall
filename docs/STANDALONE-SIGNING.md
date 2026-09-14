# Standalone trust requirement — planned release work

The preview.2 executable was blocked by Windows Application Control before process startup. Local Code Integrity events 3033/3077 and the blocked binary remain under the isolated build/evidence paths described in [the living-memory report](LIVING-MEMORY-THOUGHT.md). Editor Play Mode is a separately labelled development runtime and is not a standalone launch receipt.

Microsoft's Smart App Control guidance requires an RSA code-signing certificate from a trusted provider; self-signing is not equivalent, and ECC signatures are not supported by that check. SignTool or an appropriate trusted signing service can apply the publisher signature. See [Microsoft's signing guidance](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control), checked 14 September 2026.

Future release work must establish the authorized publisher identity and trusted signing provider, produce a separate signed artifact from a reviewed exact source, retain its file hashes and signature verification, and test the distributed executable with Smart App Control still On. Keep any certificate/private-key material in its approved signing boundary. Enrollment, purchasing, identity verification and release publication are not completed by this development slice.

The acceptance gate remains an actual policy-on standalone launch followed by the real memory/event/thought/HUD flow. A successful build or signature verification alone cannot establish it. No security-policy changes, blocked-binary relocation or alternative launch tricks form part of this workflow.

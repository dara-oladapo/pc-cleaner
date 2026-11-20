# Security policy

## Reporting a vulnerability

Please report security problems privately, not in a public issue.

Use GitHub's **Report a vulnerability** button on this repository's **Security** tab. It opens a private
report that only the maintainers can see.

Please include what you found, how to reproduce it, and which version you used. We will reply as soon as
we can, keep you updated while we work on a fix, and credit you in the release notes if you would like that.

## What counts

PC Cleaner reads your disk and removes files, so these matter most:

- a way to make it delete or move something it should not, including files outside what the user ticked;
- a way to bypass a confirmation;
- a way for another program or a crafted file or folder name to make it act outside the scanned folders;
- a problem in the installer or the update check that could deliver a tampered build.

## Supported versions

Only the latest release is supported. The app checks for updates in About.

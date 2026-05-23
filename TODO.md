# TODO.md — Known issues and bugs

Pre-existing bugs and known issues not yet triaged into the roadmap. Items here are waiting for scheduling or architectural clarity.

---

## Attach bearer token to `TrackNew.razor` WAV upload

`DeepDrftManager/Components/Pages/Tracks/TrackNew.razor` at line ~137 POSTs to `api/cms/track` without an `Authorization` header. `CmsUploadController` carries `[Authorize(Roles = "Admin")]`, so uploads return 401 in production. **Fix:** inject `IAuthSession`, copy the `AttachBearerAsync(HttpClient)` helper from the sibling `TrackEdit.razor`, call it on the client immediately after `HttpClientFactory.CreateClient("DeepDrft.API")`. Pre-existing issue, not a regression from the 10.3.33 upgrade. `IAuthSession` is now globally available, so the fix is trivial. A worktree (`tracknew-bearer`) and pending session task already exist for this; the file note here is the persistent record in case the session ends before it lands.

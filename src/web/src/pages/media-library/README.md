### Merge media libraries

You can merge one media library into another from the media library list page:

- Open `Media Library` page
- In the row's More menu (···), choose "Merge into another media library"
- Select the target media library (Media Library V2)
- The app will move all resources (data only) to the target
- You can choose to delete the source media library after merge

Notes:
- This uses the existing `PUT /resource/move` API to reassign resources' `mediaLibraryId`.
- File paths are not moved on disk; only resource ownership changes.

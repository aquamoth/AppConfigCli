We are writing a tool in c# net9, that downloads a configuration section from azure app config and displays it as a key / value list.
The user can edit all values, and add new keys by just using the keyboard. No mouse interaction is required. Existing keys can be deleted, but with a warning.

When the user saves the changes, they are uploaded to Azure and merged.


namespace Yasda.Modules.Knowledge.Domain;

// Where an article is in its review. Draft → InReview → Published; Archived hides it from readers.
internal enum ArticleStatus { Draft, InReview, Published, Archived }

// Who may read a published article: everyone signed in, or staff only.
internal enum ArticleVisibility { Public, Internal }

// The things worth remembering about an article, one history row each.
internal enum ArticleAction { Created, Edited, Submitted, Rejected, Published, Archived, Restored, EditorAdded, EditorRemoved }

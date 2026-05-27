namespace Delta.DocGen.Pipeline;

public enum FailureCategory
{
    None,
    UserError,      // bad config, missing files, invalid input
    InternalError,  // invariant violations (ID collisions, etc.)
}

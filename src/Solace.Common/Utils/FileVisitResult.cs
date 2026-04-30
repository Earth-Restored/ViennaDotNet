namespace ViennaDotNet.Common.Utils;

public enum FileVisitResult
{
    /// <summary>
    /// Continue. When returned from a <see cref="FileVisitor.PreVisitDirectory"/> method then the entries in the directory should also be visited.
    /// </summary>
    CONTINUE,
    /// <summary>
    /// Terminate.
    /// </summary>
    TERMINATE,
    /// <summary>
    /// Continue without visiting the entries in this directory. This result is only meaningful when returned from the <see cref="FileVisitor.PreVisitDirectory"/> method; otherwise this result type is the same as returning <see cref="CONTINUE"/>
    /// </summary>
    SKIP_SUBTREE,
    /// <summary>
    /// Continue without visiting the <em>siblings</em> of this file or directory.<br></br>
    /// If returned from the <see cref="FileVisitor.PreVisitDirectory"/> method then the entries in the directory are also skipped and the <see cref="FileVisitor.PostVisitDirectory"/> method is not invoked.
    /// </summary>
    SKIP_SIBLINGS
}

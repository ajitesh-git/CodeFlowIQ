namespace CodeFlowIQ.Core.Indexing;

public interface ILanguageDetector
{
    string Detect(string filePath);
}

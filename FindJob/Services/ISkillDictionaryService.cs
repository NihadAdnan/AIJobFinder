namespace FindJob.Services;

public interface ISkillDictionaryService
{
    string Normalize(string skill);
    (List<string> Matched, List<string> Missing) CompareSkills(IEnumerable<string> candidateSkills, IEnumerable<string> targetSkills);
    bool IsSkillMatch(string candidateSkill, string targetSkill);
}

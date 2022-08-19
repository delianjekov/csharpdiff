﻿using System.Text.RegularExpressions;

namespace CSharpDiff
{
    public class Diff
    {
        public bool UseLongestToken { get; set; } = true;

        public IList<DiffResult> diff(string oldString, string newString)
        {
            var cleanOldString = removeEmpty(tokenize(oldString));
            var cleanNewString = removeEmpty(tokenize(newString));
            return determineDiff(oldString, newString, cleanOldString, cleanNewString);
        }

        public IList<DiffResult> determineDiff(string oldString, string newString, string[] cleanOldString, string[] cleanNewString)
        {

            var diffs = new List<DiffResult>();
            var newLen = cleanNewString.Length;
            var oldLen = cleanOldString.Length;
            var editLength = 1;
            var maxEditLength = newLen + oldLen;

            var bestPath = new Dictionary<int, BestPath>();
            bestPath.Add(0, new BestPath
            {
                newPos = -1
            });

            var oldPos = extractCommon(bestPath[0], cleanNewString, cleanOldString, 0);
            if (bestPath[0].newPos + 1 >= newLen && oldPos + 1 >= oldLen)
            {
                diffs.Add(new DiffResult
                {
                    value = join(cleanNewString),
                    count = cleanNewString.Length
                });
            }

            while (editLength <= maxEditLength)
            {
                var dPath = -1 * editLength;
                for (var diagonalPath = dPath; diagonalPath <= editLength; diagonalPath += 2)
                {
                    BestPath basePath;
                    Console.WriteLine(diagonalPath);
                    var addPath = bestPath.ContainsKey(diagonalPath - 1) ? bestPath[diagonalPath - 1] : null;
                    var removePath = bestPath.ContainsKey(diagonalPath + 1) ? bestPath[diagonalPath + 1] : null;
                    oldPos = (removePath != null ? removePath.newPos : 0) - diagonalPath;

                    if (addPath != null)
                    {
                        // No one else is going to attempt to use this value, clear it
                        bestPath[diagonalPath - 1] = null;
                    }

                    var canAdd = addPath != null && addPath.newPos + 1 < newLen;
                    var canRemove = removePath != null && 0 <= oldPos && oldPos < oldLen;
                    if (!canAdd && !canRemove)
                    {
                        if (bestPath.ContainsKey(diagonalPath))
                        {
                            bestPath[diagonalPath] = null;
                        }
                        else
                        {
                            bestPath.Add(diagonalPath, null);
                        }

                        continue;
                    }

                    // Select the diagonal that we want to branch from. We select the prior
                    // path whose position in the new string is the farthest from the origin
                    // and does not pass the bounds of the diff graph
                    if (!canAdd || (canRemove && addPath.newPos < removePath.newPos))
                    {
                        basePath = clonePath(removePath);
                        basePath.components = pushComponent(basePath.components, null, true);
                    }
                    else
                    {
                        basePath = addPath; // No need to clone, we've pulled it from the list
                        basePath.newPos++;
                        basePath.components = pushComponent(basePath.components, true, null);
                    }

                    oldPos = extractCommon(basePath, cleanNewString, cleanOldString, diagonalPath);

                    // If we have hit the end of both strings, then we are done
                    if (basePath.newPos + 1 >= newLen && oldPos + 1 >= oldLen)
                    {
                        editLength = maxEditLength;
                        return buildValues(basePath.components, cleanNewString, cleanOldString, false);
                        // return done(buildValues(self, basePath.components, newString, oldString, UseLongestToken));
                    }
                    else
                    {

                        if (bestPath.ContainsKey(diagonalPath))
                        {
                            bestPath[diagonalPath] = basePath;
                        }
                        else
                        {
                            bestPath.Add(diagonalPath, basePath);
                        }
                        // Otherwise track this path as a potential candidate and continue.

                    }
                }

                editLength++;
            }

            return diffs;

        }

        public List<DiffResult> buildValues(List<DiffResult> components, string[] newString, string[] oldString, bool useLongestToken)
        {
            var componentPos = 0;
            var componentLen = components.Count();
            var newPos = 0;
            var oldPos = 0;

            for (; componentPos < componentLen; componentPos++)
            {
                var component = components[componentPos];
                if (component.removed != null)
                {
                    if (component.added != null && useLongestToken)
                    {
                        var value = newString.Skip(newPos).Take(newPos + (int)component.count);
                        value = value.Select((value, i) =>
                        {
                            var oldValue = oldString[oldPos + i];
                            // @todo wtf is this
                            return oldValue;
                            // return oldValue.Length > value.Length ? oldValue : value;
                        });

                        component.value = join(value.ToArray());
                    }
                    else
                    {
                        component.value = join(newString.Skip(newPos).Take(newPos + (int)component.count).ToArray());
                    }

                    newPos += (int)component.count;

                    // Common case
                    if (component.added == null || component.added == false)
                    {
                        oldPos += (int)component.count;
                    }
                }
                else
                {
                    component.value = join(oldString.Skip(oldPos).Take(oldPos + (int)component.count).ToArray());
                    oldPos += (int)component.count;

                    // Reverse add and remove so removes are output first to match common convention
                    // The diffing algorithm is tied to add then remove output and this is the simplest
                    // route to get the desired output with minimal overhead.
                    if (componentPos > 0 && components[componentPos - 1].added == true)
                    {
                        var tmp = components[componentPos - 1];
                        components[componentPos - 1] = components[componentPos];
                        components[componentPos] = tmp;
                    }
                }
            }

            // Special case handle for when one terminal is ignored (i.e. whitespace).
            // For this case we merge the terminal into the prior string and drop the change.
            // This is only available for string mode.
            var lastComponent = components[componentLen - 1];
            if (componentLen > 1
                // && typeof (lastComponent.value) === 'string' (@todo idk what this is)
                && (lastComponent.added == true || lastComponent.removed == true)
                && equals("", lastComponent.value))
            {
                components[componentLen - 2].value += lastComponent.value;
                components.RemoveAt(components.Count() - 1);
            }

            return components;
        }

        public List<DiffResult> pushComponent(List<DiffResult> components, bool? added, bool? removed)
        {

            var newComponents = new List<DiffResult>(components);
            var last = components.Any() ? components.Last() : null;
            if (last != null && last.added == added && last.removed == removed)
            {
                // We need to clone here as the component clone operation is just
                // as shallow array clone
                newComponents[components.Count() - 1] = new DiffResult { count = last.count + 1, added = added, removed = removed };
            }
            else
            {
                newComponents.Add(new DiffResult { count = 1, added = added, removed = removed });
            }
            return newComponents;
        }

        public BestPath clonePath(BestPath path)
        {
            return new BestPath
            {
                newPos = path.newPos,
                components = new List<DiffResult>(path.components)
            };
        }

        public string[] tokenize(string value)
        {
            return value.ToCharArray().Select(c => c.ToString()).ToArray();
        }

        public string join(string[] chars)
        {
            return String.Join("", chars);
        }

        public string[] removeEmpty(string[] array)
        {
            // return array;
            var ret = new List<string>();
            for (var i = 0; i < array.Count(); i++)
            {
                if (!String.IsNullOrEmpty(array.ElementAt(i)))
                {
                    ret.Add(array[i]);
                }
            }
            return ret.ToArray();
        }

        public int extractCommon(BestPath basePath, string[] newString, string[] oldString, int diagonalPath)
        {
            var newLen = newString.Length;
            var oldLen = oldString.Length;
            var newPos = basePath.newPos;
            var oldPos = newPos - diagonalPath;

            var commonCount = 0;
            while (newPos + 1 < newLen && oldPos + 1 < oldLen && equals(newString[newPos + 1], oldString[oldPos + 1]))
            {
                newPos++;
                oldPos++;
                commonCount++;
            }

            if (commonCount > 0)
            {
                basePath.components.Add(new DiffResult
                {
                    count = commonCount
                });
            }

            basePath.newPos = newPos;
            return oldPos;
        }

        public bool equals(char left, char right)
        {
            return left == right;
        }

        public bool equals(string left, string right)
        {
            return left == right;
        }
    }

    public class DiffLines : Diff
    {
        public new IList<DiffResult> diff(string oldString, string newString)
        {
            var cleanOldString = removeEmpty(tokenize(oldString));
            var cleanNewString = removeEmpty(tokenize(newString));
            return determineDiff(oldString, newString, cleanOldString, cleanNewString);
        }

        public new string[] tokenize(string value)
        {
            var retLines = new List<string>();
            var regex = new Regex("(\n|\r\n)");
            var linesAndNewlines = regex.Split(value).ToList();

            // Ignore the final empty token that occurs if the string ends with a new line
            if (String.IsNullOrEmpty(linesAndNewlines[linesAndNewlines.Count() - 1]))
            {
                linesAndNewlines.RemoveAt(linesAndNewlines.Count() - 1);
            }

            // Merge the content and line separators into single tokens
            for (var i = 0; i < linesAndNewlines.Count(); i++)
            {
                var line = linesAndNewlines[i];

                // if (i % 2 && !this.options.newlineIsToken)
                if (i % 2 == 1)
                {
                    retLines[retLines.Count() - 1] += line;
                }
                else
                {
                    line = line.Trim();
                    retLines.Add(line);
                }
            }

            return retLines.ToArray();
        }
    }

    public class DiffResult
    {
        public string value { get; set; }
        public int? count { get; set; }
        public bool? added { get; set; }
        public bool? removed { get; set; }
        public string[] lines { get; set; }
    }

    public class BestPath
    {
        public BestPath()
        {
            components = new List<DiffResult>();
        }

        public int newPos { get; set; }
        public List<DiffResult> components { get; set; }
    }

    public class Hunk
    {
        public int oldStart { get; set; }
        public int oldLines { get; set; }
        public int newStart { get; set; }
        public int newLines { get; set; }
        public string[] lines { get; set; }
    }
}
using System.Collections.Generic;

namespace Shatter.Core
{
    /// <summary>
    /// Tracks which fragments are physically adjacent to which others, and
    /// which are "anchored" (structurally fixed - touching the ground, a
    /// foundation, etc). This is what makes cluster splitting behave like a
    /// structure instead of a bag of independent pieces: removing a fragment
    /// can disconnect a whole sub-group from every anchor, at which point
    /// that sub-group should fall as one dynamic piece rather than staying
    /// frozen in place.
    /// </summary>
    public class ConnectivityGraph
    {
        private readonly Dictionary<int, HashSet<int>> edges = new Dictionary<int, HashSet<int>>();
        private readonly HashSet<int> anchoredNodes = new HashSet<int>();

        public IEnumerable<int> Nodes => edges.Keys;
        public int NodeCount => edges.Count;

        public void AddNode(int id)
        {
            if (!edges.ContainsKey(id))
                edges[id] = new HashSet<int>();
        }

        public void AddEdge(int a, int b)
        {
            AddNode(a);
            AddNode(b);
            if (a == b) return;
            edges[a].Add(b);
            edges[b].Add(a);
        }

        public void MarkAnchored(int id) => anchoredNodes.Add(id);
        public bool IsAnchored(int id) => anchoredNodes.Contains(id);

        public IReadOnlyCollection<int> GetNeighbors(int id) =>
            edges.TryGetValue(id, out var set) ? (IReadOnlyCollection<int>)set : System.Array.Empty<int>();

        /// <summary>Removes a node and every edge touching it - use when a fragment is destroyed/detached.</summary>
        public void RemoveNode(int id)
        {
            if (!edges.TryGetValue(id, out var neighbors)) return;
            foreach (var n in neighbors)
                edges[n].Remove(id);
            edges.Remove(id);
            anchoredNodes.Remove(id);
        }

        /// <summary>Returns each connected component of the current graph as a list of node ids.</summary>
        public List<List<int>> GetConnectedComponents()
        {
            var visited = new HashSet<int>();
            var components = new List<List<int>>();

            foreach (var start in edges.Keys)
            {
                if (visited.Contains(start)) continue;

                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(start);
                visited.Add(start);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);
                    foreach (int neighbor in edges[current])
                    {
                        if (visited.Contains(neighbor)) continue;
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }

                components.Add(component);
            }

            return components;
        }

        public bool ComponentHasAnchor(List<int> component)
        {
            foreach (var id in component)
                if (anchoredNodes.Contains(id))
                    return true;
            return false;
        }

        public ConnectivityGraph Clone()
        {
            var clone = new ConnectivityGraph();
            foreach (var kv in edges)
                clone.edges[kv.Key] = new HashSet<int>(kv.Value);
            foreach (var a in anchoredNodes)
                clone.anchoredNodes.Add(a);
            return clone;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet {
    public class PriorityQueue<P,T> where P : IComparable<P> {

        public class Node : IComparable<Node> {

            private int idx;
            private P priority;

            internal Node(PriorityQueue<P,T> queue, int idx, P prio, T value) {
                Queue = queue;
                this.idx = idx;
                priority = prio;
                Value = value;
            }

            internal void SwapWith(Node other) {
                Queue.nodes[other.idx] = this;
                Queue.nodes[idx] = other;

                int i = other.idx;
                other.idx = i;
                idx = i;
            }

            public void Remove() {
                SwapWith(Queue.nodes[Queue.Count-1]);
                Queue.MaxHeapify(0);
                Queue.version++;
            }

            public int CompareTo(Node other) => priority.CompareTo(other.priority);

            public PriorityQueue<P,T> Queue { get; }
            public P Priority {
                get => priority;
                set {
                    priority = value;

                    int i = idx;
                    Queue.BuildMaxHeap(i);
                    Queue.MaxHeapify(i);
                    Queue.version++;
                }
            }

            public T Value { get; set; }

        }

        private ulong version = 0;
        private List<Node> nodes = new List<Node>();

        public Node Enqueue(P prio, T val) {
            Node n = new Node(this, nodes.Count, prio, val);
            nodes.Add(n);
            BuildMaxHeap(nodes.Count - 1);
            version++;
            return n;
        }

        public Node Dequeue() {
            if(nodes.Count <= 0) throw new InvalidOperationException("The queue is empty");
            Node n = nodes[0];
            n.Remove();
            version++;
            return n;
        }

        private void BuildMaxHeap(int idx) {
            for (; idx >= 0 && nodes[(idx-1)/2].Priority.CompareTo(nodes[idx].Priority) < 0; idx = (idx-1)/2) {
                nodes[idx].SwapWith(nodes[(idx-1)/2]);
                MaxHeapify(idx);
            }
        }

        private void MaxHeapify(int idx) {
            int l = idx*2+1, r = idx*2+2;
            int maxPrio = idx;
            if (l < nodes.Count && nodes[maxPrio].Priority.CompareTo(nodes[l].Priority) < 0) maxPrio = l;
            if (r < nodes.Count && nodes[maxPrio].Priority.CompareTo(nodes[r].Priority) < 0) maxPrio = r;
            if (maxPrio != idx) {
                nodes[idx].SwapWith(nodes[maxPrio]);
                MaxHeapify(maxPrio);
            }
        }

        public int Count => nodes.Count;
    }
}
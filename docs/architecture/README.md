# Architecture

Schémas d'architecture de HouseFlow et de son équipe d'agents.

- [`hf-dispatch-states.md`](hf-dispatch-states.md) : diagrammes d'états (Mermaid, affichés par GitHub) d'une PR et d'une issue dans le circuit des agents.
- [`hf-dispatch-flow.bpmn`](hf-dispatch-flow.bpmn) : processus BPMN 2.0, un couloir par acteur. À ouvrir et modifier avec [bpmn.io](https://demo.bpmn.io) (glisser le fichier) ou Camunda Modeler.

Conventions : Mermaid pour les diagrammes d'états, bpmn.io pour le BPMN. Les deux décrivent d'abord l'existant ; la version cible corrigée par Julien sert ensuite de spécification aux tests de `hf-dispatch`.

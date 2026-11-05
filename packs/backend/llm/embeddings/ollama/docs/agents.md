## Embeddings: Ollama

- Embeddings come from a local model served by Ollama, through `OllamaSharp`. Only `src/BuildingBlocks/Llm/Wiring/OllamaEmbeddingGenerator.cs` names it; every other piece of code takes `IEmbeddingGenerator<string, Embedding<float>>`.
- The AppHost runs the server (`ollama`) and pulls the model (`ollama-embedding-model`, `nomic-embed-text`, 768 dimensions). Changing it to a model of another size is a decision: stored vectors must be made again, in tables of the new size. Never hard-code a model name in a module.
- Tests never call the model: they stand in for `IEmbeddingGenerator`, and the integration tests remove the Ollama resources.

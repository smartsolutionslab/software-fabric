## Embeddings: OpenAI

- Embeddings come from OpenAI through Microsoft's `Microsoft.Extensions.AI.OpenAI` adapter. Only `src/BuildingBlocks/Llm/Wiring/OpenAIEmbeddingGenerator.cs` names the provider; every other piece of code takes `IEmbeddingGenerator<string, Embedding<float>>`.
- The key is the AppHost's secret parameter `openai-api-key`; never write it anywhere in the repository, a log or a test.
- The model is the AppHost's parameter `openai-embedding-model`, `text-embedding-3-small` (1536 dimensions) by default. Changing it to a model of another size is a decision: stored vectors must be made again, in tables of the new size. Never hard-code a model id in a module.
- Tests never call the model: they stand in for `IEmbeddingGenerator`.

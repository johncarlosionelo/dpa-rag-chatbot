SHELL := /bin/bash
PORT ?= 5199
COLLECTION := dpa_sections_minilm
API := http://127.0.0.1:$(PORT)

.DEFAULT_GOAL := help

.PHONY: help infra build app dev test check stop status stress clean

help: ## show this
	@grep -hE '^[a-z-]+:.*?## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS=":.*?## "}; {printf "  %-10s %s\n", $$1, $$2}'

infra: ## start the vector database
	@colima status 2>/dev/null | grep -q running || colima start --cpu 2 --memory 4 --disk 20 >/dev/null
	@docker start dpa-qdrant >/dev/null 2>&1 || docker run -d --name dpa-qdrant \
		-p 6333:6333 -p 6334:6334 -v dpa_qdrant:/qdrant/storage qdrant/qdrant:latest >/dev/null
	@sleep 3
	@echo "qdrant up on 6333"

build: ## compile the backend and the client
	@dotnet build --nologo -v quiet
	@cd web && npm run build

app: build ## compile then run the app on $(PORT)
	@dotnet run --project src/Dpa.Rag.Api

dev: infra ## start the vector database then the app, this is the demo path
	@ASPNETCORE_URLS=http://127.0.0.1:$(PORT) dotnet run --project src/Dpa.Rag.Api

test: ## fast tier, about six seconds, no model calls
	@bash scripts/verify_all.sh

check: ## prove the vector database holds the corpus
	@curl -s http://127.0.0.1:6333/collections/$(COLLECTION) | python3 -c "import json,sys; d=json.load(sys.stdin)['result']; v=d['config']['params']['vectors']; print(f\"points {d['points_count']}, {v['size']} dim, {v['distance']}, status {d['status']}\")"

stress: ## live model tier, minutes not seconds
	@$(MAKE) infra
	@echo "start a second instance with: LLM_CHAIN=ollama ASPNETCORE_URLS=http://127.0.0.1:5310 dotnet run --project src/Dpa.Rag.Api"
	@echo "then: CHAT_URL=http://127.0.0.1:5310/api/chat STRESS_DEADLINE=2400 STRESS_WORKERS=2 python3 scripts/stress_test.py"

status: ## what is running
	@printf 'qdrant  '; nc -z 127.0.0.1 6333 && echo up || echo down
	@printf 'ollama  '; nc -z 127.0.0.1 11434 && echo up || echo down
	@printf 'app     '; nc -z 127.0.0.1 $(PORT) && echo up || echo down

stop: ## stop everything
	@-pkill -f "Dpa.Rag.Api"
	@-pkill -f "ollama serve"
	@-colima stop >/dev/null 2>&1
	@echo "stopped"

clean: ## stop everything and drop local caches
	@$(MAKE) stop
	@rm -rf data/section_index.json src/Dpa.Rag.Api/wwwroot
	@echo "caches cleared"
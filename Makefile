SHELL := /bin/bash
PORT ?= 5199
COLLECTION := dpa_sections_minilm
API := http://127.0.0.1:$(PORT)

.DEFAULT_GOAL := help

.PHONY: help infra build app dev local secrets test check stop status stress clean

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
	@set -a; [ -f .env ] && . ./.env; set +a; \
	printf 'groq key  '; [ -n "$$GROQ_API_KEY" ] && echo loaded || echo none, local model only; \
	printf 'ollama   '; nc -z -G1 127.0.0.1 11434 2>/dev/null && echo up || echo down; \
	ASPNETCORE_URLS=http://127.0.0.1:$(PORT) dotnet run --project src/Dpa.Rag.Api

secrets: ## show whether a cloud key is available, never prints it
	@set -a; [ -f .env ] && . ./.env; set +a; \
	printf 'groq key  '; [ -n "$$GROQ_API_KEY" ] && echo loaded || echo none, local model only; \
	printf 'ollama   '; nc -z -G1 127.0.0.1 11434 2>/dev/null && echo up || echo down, local model unavailable

local: ## start the local model, only needed as a fallback
	@OLLAMA_BIN=$$(command -v ollama || echo /opt/homebrew/opt/ollama/bin/ollama); \
	if nc -z -G1 127.0.0.1 11434 2>/dev/null; then echo "ollama already up"; \
	else nohup $$OLLAMA_BIN serve > /tmp/dpa-ollama.log 2>&1 & \
	for i in $$(seq 1 30); do nc -z -G1 127.0.0.1 11434 2>/dev/null && break; sleep 1; done; \
	echo "ollama started, log at /tmp/dpa-ollama.log"; fi

test: ## fast tier, about six seconds, no model calls
	@bash scripts/verify_all.sh

check: ## prove the vector database holds the corpus
	@curl -s http://127.0.0.1:6333/collections/$(COLLECTION) | python3 -c "import json,sys; d=json.load(sys.stdin)['result']; v=d['config']['params']['vectors']; print(f\"points {d['points_count']}, {v['size']} dim, {v['distance']}, status {d['status']}\")"

stress: ## live model tier, minutes not seconds
	@$(MAKE) infra
	@echo "start a second instance with: LLM_CHAIN=ollama ASPNETCORE_URLS=http://127.0.0.1:5310 dotnet run --project src/Dpa.Rag.Api"
	@echo "then: CHAT_URL=http://127.0.0.1:5310/api/chat STRESS_DEADLINE=2400 STRESS_WORKERS=2 python3 scripts/stress_test.py"

status: ## what is running
	@for pair in "qdrant 6333" "ollama 11434" "app $(PORT)"; do \
		set -- $$pair; \
		if nc -z -G1 127.0.0.1 $$2 >/dev/null 2>&1; then printf '%s up\n' "$$1"; \
		else printf '%s down\n' "$$1"; fi; \
	done

stop: ## stop everything
	@-pkill -f "Dpa.Rag.Api"
	@-pkill -f "ollama serve"
	@-colima stop >/dev/null 2>&1
	@echo "stopped"

clean: ## stop everything and drop local caches
	@$(MAKE) stop
	@rm -rf data/section_index.json src/Dpa.Rag.Api/wwwroot
	@echo "caches cleared"
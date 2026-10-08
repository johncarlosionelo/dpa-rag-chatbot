SHELL := /bin/bash
PORT ?= 5199
COLLECTION := dpa_sections_minilm
LINK := http://127.0.0.1:$(PORT)
OLLAMA_LOG := /tmp/dpa-ollama.log

.DEFAULT_GOAL := help

.PHONY: help infra build app dev open local secrets test check stop status stress clean

help: ## show this
	@grep -hE '^[a-z-]+:.*?## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS=":.*?## "}; {printf "  %-10s %s\n", $$1, $$2}'

infra: ## start the vector database
	@if ! colima status 2>/dev/null | grep -q running; then \
		colima start --cpu 2 --memory 4 --disk 20 >/dev/null 2>&1; \
	fi
	@docker start dpa-qdrant >/dev/null 2>&1 || docker run -d --name dpa-qdrant \
		-p 6333:6333 -p 6334:6334 -v dpa_qdrant:/qdrant/storage qdrant/qdrant:latest >/dev/null 2>&1
	@for i in $$(seq 1 30); do nc -z -G1 127.0.0.1 6333 >/dev/null 2>&1 && break; sleep 1; done
	@echo "  qdrant    up"

build: ## compile the backend and the client
	@dotnet build --nologo -v quiet
	@cd web && npm run build >/dev/null 2>&1

app: build ## compile then run the app
	@dotnet run --project src/Dpa.Rag.Api

dev: infra local ## start everything, this is the only command you need
	@if nc -z -G1 127.0.0.1 $(PORT) >/dev/null 2>&1; then \
		echo "  app       already running"; \
		set -a; [ -f .env ] && . ./.env; set +a; \
		printf '  groq      '; [ -n "$$GROQ_API_KEY" ] && echo loaded || echo none, local model only; \
		$(MAKE) --no-print-directory announce; \
		exit 0; \
	fi
	@set -a; [ -f .env ] && . ./.env; set +a; \
	printf '  groq      '; [ -n "$$GROQ_API_KEY" ] && echo loaded || echo none, local model only; \
	$(MAKE) --no-print-directory announce; \
	( sleep 4; $(MAKE) --no-print-directory open >/dev/null 2>&1 ) & \
	ASPNETCORE_URLS=$(LINK) dotnet run --project src/Dpa.Rag.Api

announce: ## print the link, clickable
	@printf '  \033[7m open the chat -> %s \033[0m\n' '$(LINK)'

open: ## open the app in the browser
	@open $(LINK)

local: ## start the local model, the fallback
	@OLLAMA_BIN=$$(command -v ollama || echo /opt/homebrew/opt/ollama/bin/ollama); \
	if nc -z -G1 127.0.0.1 11434 >/dev/null 2>&1; then echo "  ollama    up"; \
	else \
		nohup $$OLLAMA_BIN serve > $(OLLAMA_LOG) 2>&1 & \
		for i in $$(seq 1 40); do nc -z -G1 127.0.0.1 11434 >/dev/null 2>&1 && break; sleep 1; done; \
		if nc -z -G1 127.0.0.1 11434 >/dev/null 2>&1; then echo "  ollama    up"; \
		else echo "  ollama    failed, log at $(OLLAMA_LOG)"; fi; \
	fi

secrets: ## show whether a cloud key is available, never prints it
	@set -a; [ -f .env ] && . ./.env; set +a; \
	printf '  groq      '; [ -n "$$GROQ_API_KEY" ] && echo loaded || echo none, local model only

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
		if nc -z -G1 127.0.0.1 $$2 >/dev/null 2>&1; then printf '  %-9s up\n' "$$1"; \
		else printf '  %-9s down\n' "$$1"; fi; \
	done

stop: ## stop everything
	@pkill -f "make dev" >/dev/null 2>&1 || true
	@pkill -f "dotnet run --project src/Dpa.Rag.Api" >/dev/null 2>&1 || true
	@pkill -f "Dpa.Rag.Api" >/dev/null 2>&1 || true
	@pkill -f "ollama serve" >/dev/null 2>&1 || true
	@for i in $$(seq 1 20); do \
		nc -z -G1 127.0.0.1 $(PORT) >/dev/null 2>&1 || nc -z -G1 127.0.0.1 11434 >/dev/null 2>&1 || break; \
		sleep 0.5; \
	done
	@-colima stop >/dev/null 2>&1
	@leftovers=0; \
	for port in $(PORT) 11434 6333; do \
		nc -z -G1 127.0.0.1 $$port >/dev/null 2>&1 && leftovers=$$((leftovers + 1)); \
	done; \
	if [ $$leftovers -eq 0 ]; then echo "  stopped, nothing left listening"; \
	else echo "  stopped, $$leftovers port(s) still open, run make stop again"; fi

clean: ## stop everything and drop local caches
	@$(MAKE) stop
	@rm -rf data/section_index.json src/Dpa.Rag.Api/wwwroot
	@echo "  caches cleared"
# Tika Server with Tesseract, plus the Persian language data the stock image does not carry
# (section 8.1, risk R2). Build this where the base image and Debian packages are reachable, or
# mirror it (risk R5).
ARG TIKA_IMAGE=apache/tika:3.2.3.0-full
FROM ${TIKA_IMAGE}

USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends tesseract-ocr-fas tesseract-ocr-eng curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*

# The distro packages carry tessdata_fast, whose Persian model misreads a large share of words.
# Swap in tessdata_best (slower, far more accurate). Set TESSDATA_URL to a mirror, or to empty to
# keep the packaged models when GitHub is unreachable (risk R5).
ARG TESSDATA_URL=https://github.com/tesseract-ocr/tessdata_best/raw/main
RUN set -eu; \
    dir=$(dirname "$(find /usr/share/tesseract-ocr -name eng.traineddata | head -n 1)"); \
    if [ -n "$TESSDATA_URL" ]; then \
      for lang in fas eng osd; do \
        curl -fsSL --retry 3 -o "/tmp/$lang.traineddata" "$TESSDATA_URL/$lang.traineddata"; \
        mv "/tmp/$lang.traineddata" "$dir/$lang.traineddata"; \
      done; \
    fi
USER 35002:35002

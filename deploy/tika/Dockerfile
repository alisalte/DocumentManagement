# Tika Server with Tesseract, plus the Persian language data the stock image does not carry
# (section 8.1, risk R2). Build this where the base image and Debian packages are reachable, or
# mirror it (risk R5).
ARG TIKA_IMAGE=apache/tika:3.2.3.0-full
FROM ${TIKA_IMAGE}

USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends tesseract-ocr-fas tesseract-ocr-eng \
 && rm -rf /var/lib/apt/lists/*
USER 35002:35002

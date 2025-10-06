<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:wix="http://schemas.microsoft.com/wix/2006/wi">

	<xsl:output method="xml" indent="yes"/>

	<!-- Standard-Kopie: alles 1:1 übernehmen -->
	<xsl:template match="@*|node()">
		<xsl:copy>
			<xsl:apply-templates select="@*|node()"/>
		</xsl:copy>
	</xsl:template>

	<!-- Nur wenn ein Component KEIN Win64-Attribut hat: Win64="yes" ergänzen -->
	<xsl:template match="wix:Component[not(@Win64)]">
		<xsl:copy>
			<xsl:copy-of select="@*"/>
			<xsl:attribute name="Win64">yes</xsl:attribute>
			<xsl:apply-templates select="node()"/>
		</xsl:copy>
	</xsl:template>

</xsl:stylesheet>
